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
/// create version, id-addressed load, run, job-failure, artifact resolve). Two capabilities the issue scopes
/// remain gated on the still-OPEN analysis content API (honua-server#1237: list + cost-estimate endpoints)
/// and are surfaced as explicit capability states rather than fabricated:
///   - Analysis-package listing: honua-server#1237 has not landed the list verb, so the workspace cannot
///     enumerate existing packages from live data. New plans and id-addressed loads work; the list binds
///     automatically once #1237 lands.
///   - Runtime/cost estimate: honua-server#1237 has not landed the server cost-estimate route, so the
///     compute estimate is a Console-side projection over the authored plan, clearly labelled as a local
///     estimate, until the server endpoint lands. The estimate still gates submit (AC#2) so the operator
///     reviews runtime/cost before a job is queued.
///   - Analysis-package dry-run preview: the server preview route is saved-query only, so a preview of an
///     analysis package is surfaced as unsupported until the server adds an analysis-package preview.
/// </summary>
public sealed class HonuaServerStudioAnalysisContentDataSource : IStudioAnalysisPackageDataSource
{
    private const string Surface = "Analysis builder";
    private const string ListContract = "GET /api/v1/analysis/content/items (list)";
    private const string EstimateContract = "POST /api/v1/analysis/content/items/{itemId}/versions/{contentVersion}/estimate";
    private const string PreviewContract = "POST /api/v1/analysis/content/items/{itemId}/versions/{contentVersion}/preview";

    private readonly IHonuaAnalysisContentClient _client;

    public HonuaServerStudioAnalysisContentDataSource(IHonuaAnalysisContentClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public Task<StudioAnalysisWorkspace> GetWorkspaceAsync(CancellationToken cancellationToken = default)
    {
        // honua-server#1237 (analysis content API: list endpoint) is still open. Surface that explicitly
        // instead of mocking a list: operators reach an analysis by id (deep link / known id) or author a
        // new plan.
        var listUnsupported = new StudioAnalysisCapabilityState(
            Surface,
            "Unsupported",
            ListContract,
            "honua-server does not yet expose an analysis-package list endpoint, so existing packages cannot "
            + "be enumerated from live data. Open a known analysis by id or create a new analysis. This list "
            + "binds automatically once honua-server#1237 adds a list route.");

        return Task.FromResult(new StudioAnalysisWorkspace([], [listUnsupported]));
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

    public Task<StudioAnalysisCommandResult> EstimateAsync(
        StudioAnalysisPlanEditor plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (!plan.IsExistingPackage)
        {
            return Task.FromResult(Failure("Save the analysis before estimating runtime and cost."));
        }

        // honua-server#1237 (analysis content API: cost-estimate endpoint) is still open. Until it lands,
        // project a transparent Console-side estimate over the authored plan so the operator still reviews
        // runtime/cost before submit (AC#2). This is NOT fabricated server data: it is a clearly-labelled
        // local projection and is accompanied by an explicit capability state documenting the missing route.
        var estimate = StudioAnalysisEstimator.Estimate(plan);
        plan.Estimate = estimate;

        var note = new StudioAnalysisCapabilityState(
            Surface,
            "Unsupported",
            EstimateContract,
            "Estimate unavailable from server: honua-server does not yet expose a runtime/cost estimate "
            + "route, so this estimate is a local Console projection over the authored plan (inputs x "
            + "parameters x compute profile), not a server-computed figure. It binds to the server estimate "
            + "once honua-server#1237 adds the route.");

        return Task.FromResult(new StudioAnalysisCommandResult(
            true,
            $"Local estimate ready: ~{estimate.EstimatedRuntimeSeconds.ToString("0.#", CultureInfo.InvariantCulture)}s "
            + $"over ~{estimate.EstimatedInputFeatures.ToString("N0", CultureInfo.InvariantCulture)} features "
            + "(server estimate route pending).",
            plan,
            note));
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
                    job.Version.Version,
                    Failure: new StudioAnalysisJobFailureView(failure.Classification, failure.Message));
            }

            return new StudioAnalysisJobView(
                job.JobId,
                job.Status,
                job.Version.Version,
                Failure: new StudioAnalysisJobFailureView("unknown", "The analysis job failed."));
        }

        return new StudioAnalysisJobView(job.JobId, job.Status, job.Version.Version);
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

        var view = StudioAnalysisPackageMapper.ToArtifactView(result.Data!.Artifact, result.Data.Binding);
        plan.SubmittedJob = (plan.SubmittedJob ?? new StudioAnalysisJobView(result.Data.Artifact.JobId, "Succeeded", plan.Version))
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
