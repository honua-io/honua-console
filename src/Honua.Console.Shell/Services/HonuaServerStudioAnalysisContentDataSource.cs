using System.Globalization;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Studio analysis-builder data source bound to the real honua-server analysis content/artifacts contract
/// (honua-server#1182) and the closed execution engine (honua-server#681/#721/#724) through the
/// <see cref="IHonuaAnalysisContentClient"/> shim. Authoring a plan creates a server content item/version;
/// submit drives the canonical geoprocessing runtime through the content "run" route; the result-artifact
/// panel resolves the produced artifact from the server. There is no in-memory analysis data in the merged
/// result (Console Patterns Charter section 11).
///
/// The core analysis content/artifacts contract (honua-server#1182) is CLOSED and bound live (create item,
/// create version, id-addressed load, run, job-failure, artifact resolve). The analysis content list +
/// cost-estimate endpoints (honua-server#1237) are now MERGED and bound live here:
///   - Analysis-package listing: the workspace enumerates active analysis packages from the server list
///     endpoint (GET .../content/items?kind=analysisPackage&amp;lifecycle=active).
///   - Runtime/cost estimate: the plan card binds the server-computed runtime/cost estimate
///     (POST .../versions/{contentVersion}/estimate) — a real server figure, not a Console projection.
///     The estimate still gates submit (AC#2) so the operator reviews runtime/cost before a job is queued.
/// One capability the issue scopes remains unsupported on the server contract and is surfaced as an
/// explicit capability state rather than fabricated:
///   - Analysis-package dry-run preview: the server preview route is saved-query only, so a preview of an
///     analysis package is surfaced as unsupported until the server adds an analysis-package preview.
/// </summary>
public sealed class HonuaServerStudioAnalysisContentDataSource : IStudioAnalysisPackageDataSource
{
    private const string Surface = "Analysis builder";
    private const string PreviewContract = "POST /api/v1/analysis/content/items/{itemId}/versions/{contentVersion}/preview";
    private const string GenerateContract = "POST /api/v1/analysis/content/generate";

    private readonly IHonuaAnalysisContentClient _client;
    private readonly IOperateTransitionDataSource? _operate;

    public HonuaServerStudioAnalysisContentDataSource(
        IHonuaAnalysisContentClient client,
        IOperateTransitionDataSource? operate = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _operate = operate;
    }

    // Catalog grounding: the real published layers so analysis generation binds a real input
    // serviceId/layerId (instead of layerId 0). Best-effort; without the catalog the model is ungrounded
    // and the input-data chart simply stays unbound. Mirrors the query/map data sources.
    private async Task<HonuaQueryGenerationSource[]> LoadAvailableSourcesAsync(CancellationToken cancellationToken)
    {
        if (_operate is null)
        {
            return [];
        }

        try
        {
            var view = await _operate.GetLayersViewAsync(cancellationToken).ConfigureAwait(false);
            return view.Services
                .SelectMany(service => service.Layers.Select(layer => new HonuaQueryGenerationSource
                {
                    ServiceId = service.Name,
                    LayerId = layer.LayerId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    Name = layer.Name,
                }))
                .ToArray();
        }
        catch (Exception)
        {
            return [];
        }
    }

    // The local model binds the real numeric layerId but is inconsistent about echoing the serviceId; the
    // serviceId is deterministic given the layerId, so recover it from the catalog (match the bound layerId,
    // else the single available source) for each input the model left unbound. This is what lets the
    // input-data chart fetch the input layer's real rows. Never invents a binding.
    private static void ResolveInputBindings(StudioAnalysisPlanEditor? plan, HonuaQueryGenerationSource[] sources)
    {
        if (plan is null || sources.Length == 0)
        {
            return;
        }

        foreach (var input in plan.Inputs)
        {
            if (!string.IsNullOrWhiteSpace(input.ServiceId))
            {
                continue;
            }

            var layerId = input.LayerId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var match = sources.FirstOrDefault(source =>
                string.Equals(source.LayerId, layerId, StringComparison.Ordinal));
            match ??= sources.Length == 1 ? sources[0] : null;
            if (match is not null && !string.IsNullOrWhiteSpace(match.ServiceId))
            {
                input.ServiceId = match.ServiceId;
            }
        }
    }

    public async Task<StudioAnalysisWorkspace> GetWorkspaceAsync(CancellationToken cancellationToken = default)
    {
        // honua-server#1237 (analysis content API: list endpoint) is merged. Enumerate the active
        // analysis packages from live data so the /studio/analysis list view shows real items.
        var result = await _client
            .ListItemsAsync(
                new HonuaAnalysisContentListQuery(
                    Kind: HonuaAnalysisContentKinds.AnalysisPackage,
                    Lifecycle: "active"),
                cancellationToken)
            .ConfigureAwait(false);

        if (result.Issue is { } issue)
        {
            return new StudioAnalysisWorkspace([], [ToCapabilityState(issue)]);
        }

        // System.Text.Json overrides the [] initializer with null when the server emits an explicit JSON null
        // for the key (same invariant the generation paths guard) — coalesce before LINQ so a null Items on
        // the list response doesn't NRE the workspace.
        var plans = (result.Data!.Items ?? [])
            .Select(StudioAnalysisPackageMapper.ToListItem)
            .ToArray();

        return new StudioAnalysisWorkspace(plans, []);
    }

    public async Task<StudioAnalysisEditorLoad> LoadAsync(
        string? analysisId,
        CancellationToken cancellationToken = default)
    {
        // A brand-new analysis opens a blank Console-owned authoring scaffold, not server data. Existing
        // packages always load their latest version from the live server.
        if (string.IsNullOrWhiteSpace(analysisId))
        {
            return new StudioAnalysisEditorLoad(StudioAnalysisPackageMapper.CreateTemplate(), []);
        }

        var result = await _client
            .GetVersionAsync(analysisId, null, cancellationToken)
            .ConfigureAwait(false);
        if (result.Issue is { } issue)
        {
            return new StudioAnalysisEditorLoad(null, [ToCapabilityState(issue)]);
        }

        return new StudioAnalysisEditorLoad(StudioAnalysisPackageMapper.ToEditorState(result.Data!), []);
    }

    public async Task<StudioAnalysisCommandResult> SaveDraftAsync(
        StudioAnalysisPlanEditor plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (plan.IsPublished)
        {
            return Failure("This analysis is published; reopen a draft before saving a new version.");
        }

        var content = StudioAnalysisPackageMapper.ToPackageContent(plan);

        HonuaAdminEndpointResult<HonuaAnalysisContentVersionResponse> result;
        if (plan.IsExistingPackage)
        {
            result = await _client
                .CreateVersionAsync(
                    plan.AnalysisId!,
                    new HonuaCreateAnalysisContentVersionRequest
                    {
                        AnalysisPackage = content,
                        BasedOnVersionId = plan.ETag
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            result = await _client
                .CreateItemAsync(
                    new HonuaCreateAnalysisContentItemRequest
                    {
                        Kind = HonuaAnalysisContentKinds.AnalysisPackage,
                        Name = BuildName(plan),
                        Title = string.IsNullOrWhiteSpace(plan.Title) ? null : plan.Title,
                        AnalysisPackage = content
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (result.Issue is { } issue)
        {
            return Failure(issue.Detail, ToCapabilityState(issue), issue.FieldErrors);
        }

        var saved = StudioAnalysisPackageMapper.ToEditorState(result.Data!);
        // A new immutable version invalidates any prior estimate; the operator re-estimates the saved plan
        // so submit reflects the runtime/cost of what will run.
        saved.Estimate = null;
        return new StudioAnalysisCommandResult(
            true,
            $"Saved analysis version {saved.Version.ToString(CultureInfo.InvariantCulture)}. Run an estimate before submit.",
            saved);
    }

    public async Task<StudioAnalysisCommandResult> EstimateAsync(
        StudioAnalysisPlanEditor plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (!plan.IsExistingPackage)
        {
            return Failure("Save the analysis before estimating runtime and cost.");
        }

        // honua-server#1237 (analysis content API: cost-estimate endpoint) is merged. Bind the
        // server-computed runtime/cost estimate so the plan card shows a real server figure, not a local
        // projection. The estimate still gates submit (AC#2) so the operator reviews runtime/cost first.
        var result = await _client
            .EstimateAsync(plan.AnalysisId!, plan.Version, cancellationToken)
            .ConfigureAwait(false);

        if (result.Issue is { } issue)
        {
            return Failure(issue.Detail, ToCapabilityState(issue), issue.FieldErrors);
        }

        // Estimate is non-null-typed with a new() initializer but deserializes to null on an explicit server
        // JSON null, so coalesce before the deref.
        var serverEstimate = result.Data!.Estimate ?? new();
        var estimate = StudioAnalysisPackageMapper.ToComputeEstimate(serverEstimate, plan.ComputeProfile);
        plan.Estimate = estimate;

        var lowerBound = serverEstimate.IsLowerBound ? " (lower bound; some input statistics unresolved)" : string.Empty;
        return new StudioAnalysisCommandResult(
            true,
            $"Server estimate ready: ~{estimate.EstimatedRuntimeSeconds.ToString("0.#", CultureInfo.InvariantCulture)}s "
            + $"over ~{estimate.EstimatedInputFeatures.ToString("N0", CultureInfo.InvariantCulture)} features"
            + $"{lowerBound}.",
            plan);
    }

    public Task<StudioAnalysisCommandResult> PreviewAsync(
        StudioAnalysisPlanEditor plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        // The server preview route is saved-query only; an analysis-package dry-run preview is not in the
        // honua-server#1182 contract. Surface it honestly rather than running a real job under "preview".
        var unsupported = new StudioAnalysisCapabilityState(
            Surface,
            "Unsupported",
            PreviewContract,
            "honua-server exposes a preview route for saved queries only; an analysis-package dry-run preview "
            + "is not yet part of honua-server#1182. Submit the job to run the analysis through the execution "
            + "engine, or wait for an analysis-package preview route.");

        return Task.FromResult(new StudioAnalysisCommandResult(
            false,
            unsupported.Detail,
            plan,
            unsupported));
    }

    public async Task<StudioAnalysisCommandResult> SubmitAsync(
        StudioAnalysisPlanEditor plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (plan.IsPublished)
        {
            return Failure("This analysis is published; reopen a draft to author a new version before submit.");
        }

        // Enforce the Console pre-submit gate (title, method, bound input, output field, fresh estimate)
        // before queuing a job on the execution engine (AC#1/AC#2).
        var readiness = StudioAnalysisPlanEvaluator.Evaluate(plan);
        if (!readiness.CanSubmit)
        {
            return Failure(
                $"Resolve {readiness.UnmetRequirements.Count.ToString(CultureInfo.InvariantCulture)} requirement(s) "
                + $"before submit: {string.Join(" ", readiness.UnmetRequirements)}");
        }

        if (!plan.IsExistingPackage)
        {
            return Failure("Save the analysis before submitting a job.");
        }

        var parameters = plan.Parameters
            .Where(p => !string.IsNullOrWhiteSpace(p.Name))
            .GroupBy(p => p.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Last().Value, StringComparer.Ordinal);

        var run = await _client
            .RunAsync(
                plan.AnalysisId!,
                plan.Version,
                new HonuaRunAnalysisContentVersionRequest
                {
                    IdempotencyKey = $"console-{plan.AnalysisId}-v{plan.Version.ToString(CultureInfo.InvariantCulture)}",
                    Parameters = parameters.Count == 0 ? null : parameters
                },
                cancellationToken)
            .ConfigureAwait(false);

        if (run.Issue is { } issue)
        {
            return Failure(issue.Detail, ToCapabilityState(issue), issue.FieldErrors);
        }

        var job = run.Data!;
        var jobView = await ResolveJobResultAsync(plan, job, cancellationToken).ConfigureAwait(false);
        plan.SubmittedJob = jobView;

        var message = jobView.HasFailure
            ? $"Job {jobView.JobId} failed: {jobView.Failure!.Message}"
            : $"Submitted job {jobView.JobId} ({jobView.Status}).";

        return new StudioAnalysisCommandResult(true, message, plan);
    }

    public async Task<StudioAnalysisGenerationOutcome> GenerateAsync(
        StudioAnalysisPlanEditor currentPlan,
        StudioAnalysisGenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(currentPlan);
        ArgumentNullException.ThrowIfNull(request);

        // A refine turn ships the current analysis package so the server edits it; a first turn ships null
        // (fresh). The "has content" probe keys off any authored intent so a blank scaffold requests fresh
        // generation.
        var hasContent = currentPlan.Inputs.Count > 0
            || currentPlan.Parameters.Count > 0
            || currentPlan.OutputSchema.Count > 0
            || !string.IsNullOrWhiteSpace(currentPlan.Goal)
            || !string.IsNullOrWhiteSpace(currentPlan.Title);

        var availableSources = await LoadAvailableSourcesAsync(cancellationToken).ConfigureAwait(false);

        var wire = new HonuaGenerateAnalysisRequest
        {
            Prompt = request.Prompt,
            Provider = request.Provider,
            Model = request.Model,
            Analysis = hasContent ? StudioAnalysisPackageMapper.ToPackageContent(currentPlan) : null,
            Conversation = request.Conversation
                .Select(turn => new HonuaAnalysisGenerationTurn { Role = turn.Role, Content = turn.Content })
                .ToArray(),
            Answers = request.Answers
                .Select(answer => new HonuaAnalysisGenerationAnswer { QuestionId = answer.QuestionId, OptionId = answer.OptionId })
                .ToArray(),
            AvailableSources = availableSources
        };

        var result = await _client.GenerateAsync(wire, cancellationToken).ConfigureAwait(false);
        if (result.Issue is { } issue)
        {
            // A 404/501 means this server lacks the analysis generation contract. Rather than a dead end,
            // start the operator from a baseline distribution of a real catalog layer when one exists
            // (honest, refinable); fall back to the plain "unsupported" notice when there is no source.
            if (issue.StatusCode is 404 or 501)
            {
                return SeedBaselinePlan(request.Prompt, availableSources)
                    ?? new StudioAnalysisGenerationOutcome
                    {
                        Status = StudioAnalysisGenerationStatuses.Unsupported,
                        Rationale = "This server does not offer AI analysis generation yet."
                    };
            }

            return StudioAnalysisGenerationOutcome.Blocked(ToCapabilityState(issue));
        }

        var outcome = MapGeneration(currentPlan, result.Data!);

        // The local model frequently reports "unsupported" for analysis (a model-capability limit, not a
        // wiring gap — the server's analysis backend is present). Rather than leave the operator at a dead
        // end, seed a baseline distribution bound to a real catalog layer so the input-data chart renders
        // the layer's REAL features. The seeded plan's rationale states plainly that it is a baseline, not
        // an AI-authored plan, so it is honest (no-mock, Charter §11).
        if (string.Equals(outcome.Status, StudioAnalysisGenerationStatuses.Unsupported, StringComparison.Ordinal)
            && SeedBaselinePlan(request.Prompt, availableSources) is { } baseline)
        {
            return baseline;
        }

        ResolveInputBindings(outcome.Plan, availableSources);
        return outcome;
    }

    // Build an INSTANT, real-data baseline analysis from the live catalog without any model call. Lets the
    // page show a rendered starting result the moment the operator sends a prompt, rather than blocking them
    // on a model round-trip that can take minutes and may report "unsupported" on this server. Returns null
    // when no catalog source is available.
    public async Task<StudioAnalysisPlanEditor?> SeedBaselineAsync(
        string? prompt,
        CancellationToken cancellationToken = default)
    {
        var sources = await LoadAvailableSourcesAsync(cancellationToken).ConfigureAwait(false);
        return BuildBaselinePlan(prompt, sources);
    }

    // Wrap the baseline plan in a "generated" outcome with an honest rationale for the server path: when the
    // model reports "unsupported", we still hand back a real, refinable starting plan instead of a dead end.
    private static StudioAnalysisGenerationOutcome? SeedBaselinePlan(
        string? prompt,
        HonuaQueryGenerationSource[] sources)
    {
        if (BuildBaselinePlan(prompt, sources) is not { } plan)
        {
            return null;
        }

        var name = plan.Inputs.Count > 0 ? plan.Title.Replace("Baseline distribution of ", string.Empty) : "your data";
        return new StudioAnalysisGenerationOutcome
        {
            Status = StudioAnalysisGenerationStatuses.Generated,
            Plan = plan,
            Rationale =
                $"AI analysis generation isn't available on this server, so I started you from a baseline "
                + $"distribution of {name} — the layer's real features. Refine the method, inputs, and "
                + "parameters, or open the editor.",
            Warnings =
            [
                "Baseline — not generated from your prompt; the local model can't draft analyses on this server."
            ],
        };
    }

    // Build a baseline analysis grounded in a real catalog layer. Binds the REAL service+layer of the source
    // the prompt names (or the first available source) so the input-data chart renders the layer's actual
    // features — nothing is fabricated. Returns null when there is no usable catalog source. Mirrors the map
    // data source's empty-result seed.
    private static StudioAnalysisPlanEditor? BuildBaselinePlan(
        string? prompt,
        HonuaQueryGenerationSource[] sources)
    {
        if (sources.Length == 0)
        {
            return null;
        }

        // Prefer a source the prompt actually names (by layer name or service id) so the baseline is about
        // the layer the operator asked for; otherwise fall back to the first available source. This only
        // selects among REAL catalog sources — it never invents one.
        var prone = prompt ?? string.Empty;
        var src = sources.FirstOrDefault(s =>
                     (!string.IsNullOrWhiteSpace(s.Name)
                         && prone.Contains(s.Name!, StringComparison.OrdinalIgnoreCase))
                     || (!string.IsNullOrWhiteSpace(s.ServiceId)
                         && prone.Contains(s.ServiceId, StringComparison.OrdinalIgnoreCase)))
                  ?? sources[0];
        if (string.IsNullOrWhiteSpace(src.ServiceId)
            || !int.TryParse(
                src.LayerId,
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out var layerId)
            || layerId < 0)
        {
            return null;
        }

        var name = string.IsNullOrWhiteSpace(src.Name) ? src.ServiceId : src.Name!;
        var plan = new StudioAnalysisPlanEditor
        {
            Title = $"Baseline distribution of {name}",
            Goal = string.IsNullOrWhiteSpace(prompt) ? $"Summarise {name}" : prompt!,
            Method = "aggregation",
        };
        plan.Inputs.Add(new StudioAnalysisInputEditor
        {
            Role = "primary",
            ServiceId = src.ServiceId,
            LayerId = layerId,
        });

        return plan;
    }

    private static StudioAnalysisGenerationOutcome MapGeneration(
        StudioAnalysisPlanEditor currentPlan,
        HonuaAnalysisGenerationResult result)
    {
        // Each collection declares a non-null initializer, but System.Text.Json overrides it with null when
        // the server emits an explicit JSON null for the key (an omitted key keeps the initializer), so every
        // collection on the deserialized result may be null. Guard each before LINQ — otherwise a perfectly
        // valid 'generated' result (which carries no clarifications/unmapped) throws and freezes the page
        // (mirrors the workflow client's defensive mapping).
        var warnings = new List<string>();
        if (result.Validation is not null)
        {
            warnings.AddRange(result.Validation.Warnings ?? []);
        }

        warnings.AddRange((result.UnmappedRequests ?? [])
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => $"No mapping for: {item}"));

        var clarifications = (result.Clarifications ?? [])
            .Select(question => new StudioConversationClarification(
                question.Id,
                string.IsNullOrWhiteSpace(question.Prompt) ? question.Kind : question.Prompt,
                question.Reason ?? string.Empty,
                (question.Choices ?? [])
                    .Select(choice => new StudioConversationChoice(choice.Id, choice.Label, choice.Effect))
                    .ToArray()))
            .ToArray();

        StudioAnalysisPlanEditor? plan = null;
        var generated = string.Equals(result.Status, StudioAnalysisGenerationStatuses.Generated, StringComparison.Ordinal);
        if (generated && result.Analysis is { } analysis)
        {
            plan = StudioAnalysisPackageMapper.ApplyGeneratedPackage(currentPlan, analysis);

            if (result.Validation is { IsValid: false })
            {
                warnings.AddRange((result.Validation.Failures ?? []).Select(failure => failure.Message));
            }
        }

        return new StudioAnalysisGenerationOutcome
        {
            Status = NormalizeStatus(result.Status),
            Plan = plan,
            Rationale = result.Rationale ?? string.Empty,
            Clarifications = clarifications,
            Warnings = warnings,
            CapabilityState = result.CapabilityState is null
                ? null
                : new StudioAnalysisGenerationCapability(
                    result.CapabilityState.Name,
                    result.CapabilityState.State,
                    result.CapabilityState.Reason),
            Provider = result.Provider,
            Model = result.Model
        };
    }

    private static string NormalizeStatus(string? status) => status switch
    {
        StudioAnalysisGenerationStatuses.Generated => StudioAnalysisGenerationStatuses.Generated,
        StudioAnalysisGenerationStatuses.NeedsClarification => StudioAnalysisGenerationStatuses.NeedsClarification,
        StudioAnalysisGenerationStatuses.Unsupported => StudioAnalysisGenerationStatuses.Unsupported,
        StudioAnalysisGenerationStatuses.Refused => StudioAnalysisGenerationStatuses.Refused,
        _ => StudioAnalysisGenerationStatuses.Error
    };

    private async Task<StudioAnalysisJobView> ResolveJobResultAsync(
        StudioAnalysisPlanEditor plan,
        HonuaAnalysisContentJobResponse job,
        CancellationToken cancellationToken)
    {
        // A terminal-failure job surfaces a safe classification on the result panel; a non-failed job that
        // already references a result artifact resolves it for the artifact panel + downstream binding.
        if (string.Equals(job.Status, "Failed", StringComparison.OrdinalIgnoreCase))
        {
            var failureResult = await _client.GetJobFailureAsync(job.JobId, cancellationToken).ConfigureAwait(false);
            if (failureResult.Data is { } failure)
            {
                return new StudioAnalysisJobView(
                    job.JobId,
                    job.Status,
                    (job.Version ?? new()).Version,
                    Failure: new StudioAnalysisJobFailureView(failure.Classification, failure.Message));
            }

            return new StudioAnalysisJobView(
                job.JobId,
                job.Status,
                (job.Version ?? new()).Version,
                Failure: new StudioAnalysisJobFailureView("unknown", "The analysis job failed."));
        }

        return new StudioAnalysisJobView(job.JobId, job.Status, (job.Version ?? new()).Version);
    }

    /// <summary>
    /// Resolves a produced artifact id into the result-artifact panel projection, including the downstream
    /// content families it can become (AC#3). Used once an analysis job materializes an artifact.
    /// </summary>
    public async Task<StudioAnalysisCommandResult> ResolveArtifactAsync(
        string artifactId,
        StudioAnalysisPlanEditor plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);
        ArgumentNullException.ThrowIfNull(plan);

        var result = await _client.GetArtifactAsync(artifactId, cancellationToken).ConfigureAwait(false);
        if (result.Issue is { } issue)
        {
            return Failure(issue.Detail, ToCapabilityState(issue), issue.FieldErrors);
        }

        // Artifact/Binding are non-null-typed with new() initializers but deserialize to null on an explicit
        // server JSON null, so coalesce before the derefs.
        var artifact = result.Data!.Artifact ?? new();
        var binding = result.Data.Binding ?? new();
        var view = StudioAnalysisPackageMapper.ToArtifactView(artifact, binding);
        plan.SubmittedJob = (plan.SubmittedJob ?? new StudioAnalysisJobView(artifact.JobId, "Succeeded", plan.Version))
            with
        { Artifact = view };

        return new StudioAnalysisCommandResult(
            true,
            $"Resolved result artifact {view.Label}.",
            plan);
    }

    private static string BuildName(StudioAnalysisPlanEditor plan)
    {
        var basis = string.IsNullOrWhiteSpace(plan.Title) ? plan.Method : plan.Title;
        var slug = new string(basis
            .ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray())
            .Trim('-');
        if (string.IsNullOrWhiteSpace(slug))
        {
            slug = "analysis";
        }

        return $"{slug}-{Guid.NewGuid():N}";
    }

    private static StudioAnalysisCommandResult Failure(
        string message,
        StudioAnalysisCapabilityState? issue = null,
        IReadOnlyList<Honua.Console.Contracts.HonuaFieldValidationError>? fieldErrors = null) =>
        new(false, message, Issue: issue, FieldErrors: fieldErrors);

    private static StudioAnalysisCapabilityState ToCapabilityState(HonuaAdminEndpointIssue issue) =>
        new(
            Surface,
            issue.State,
            issue.Contract,
            issue.StatusCode is null
                ? issue.Detail
                : $"{issue.Detail} HTTP {issue.StatusCode.Value.ToString(CultureInfo.InvariantCulture)}.");
}
