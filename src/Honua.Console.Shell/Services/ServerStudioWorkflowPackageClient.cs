using System.Globalization;
using System.Text.Json;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Server-backed <see cref="IStudioWorkflowPackageClient"/>. Binds the Studio GP/ETL workflow editor to
/// honua-server (#1185) through the <see cref="IWorkflowPackageApiClient"/> shim: the node palette, package
/// drafts, immutable versions, dry-runs, and publications/runs come from live server data - never seeded
/// fixtures. When the server cannot be reached, rejects the request, or lacks the contract, the call carries
/// a <see cref="StudioWorkflowBindingState"/> so the editor renders the shared blocked surface (charter
/// §5/§11) instead of fabricated success. Mirrors <see cref="ServerStudioAuthoringShell"/> and
/// <see cref="HonuaServerOperateTransitionDataSource"/>.
///
/// The server models a workflow package as a mutable draft graph plus immutable versions; dry-run and
/// publish operate on versions, not the mutable draft. This adapter snapshots the editor draft into the
/// server graph (server-owned: nodes, edges, schedule, worker, validation, versions, publications) and
/// round-trips editor-only authoring fields (invocation parameters, retry policy, publication intent,
/// authored output schemas, node layout) through the server's opaque editor metadata so nothing is
/// fabricated and nothing is silently dropped.
/// </summary>
public sealed class ServerStudioWorkflowPackageClient : IStudioWorkflowPackageClient
{
    internal const string Surface = "Studio workflow.package";
    internal const string EditorStateKey = "console.editorState";
    internal const string NodeLabelKey = "console.label";
    internal const string NodeColumnKey = "console.column";
    internal const string NodeRowKey = "console.row";

    private static readonly JsonSerializerOptions EditorStateOptions = new(JsonSerializerDefaults.Web);

    // Shared empty map for normalizing null collections on freshly-generated graphs (the server
    // omits empty maps via System.Text.Json source-gen, so they deserialize to null).
    private static readonly IReadOnlyDictionary<string, string> EmptyStringMap =
        new Dictionary<string, string>(StringComparer.Ordinal);

    private readonly IWorkflowPackageApiClient _api;

    public ServerStudioWorkflowPackageClient(IWorkflowPackageApiClient api)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
    }

    public async Task<StudioWorkflowEditorContext> OpenEditorAsync(
        string? draftId,
        CancellationToken cancellationToken = default)
    {
        // The node registry doubles as the binding probe: if it cannot bind, the editor renders the blocked
        // surface before any draft load (no waterfall - the page needs the palette anyway).
        var registry = await _api.GetNodeRegistryAsync(cancellationToken).ConfigureAwait(false);
        if (!registry.IsSuccess || registry.Data is null)
        {
            return new StudioWorkflowEditorContext(ToBindingState(registry.Issue), [], Draft: null);
        }

        var definitions = MapNodeDefinitions(registry.Data);

        if (string.IsNullOrWhiteSpace(draftId) ||
            string.Equals(draftId, "new", StringComparison.OrdinalIgnoreCase))
        {
            return new StudioWorkflowEditorContext(BindingState: null, definitions, CreateLocalDraft());
        }

        var package = await _api.GetPackageAsync(draftId, cancellationToken).ConfigureAwait(false);
        if (!package.IsSuccess || package.Data is null)
        {
            // A 404 is a missing draft (not a binding failure); other issues block the surface.
            if (package.Issue?.StatusCode == 404)
            {
                return new StudioWorkflowEditorContext(BindingState: null, definitions, Draft: null);
            }

            return new StudioWorkflowEditorContext(ToBindingState(package.Issue), definitions, Draft: null);
        }

        return new StudioWorkflowEditorContext(BindingState: null, definitions, ToDraft(package.Data, definitions));
    }

    public async Task<IReadOnlyList<StudioWorkflowDraftSummary>> ListDraftsAsync(
        CancellationToken cancellationToken = default)
    {
        var result = await _api.ListPackagesAsync(cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess || result.Data is null)
        {
            return [];
        }

        return result.Data.Items
            .Select(package => new StudioWorkflowDraftSummary
            {
                DraftId = package.PackageId,
                ContentItemId = package.PackageId,
                Title = package.Name,
                PackageType = StudioWorkflowContractValues.PackageType,
                VersionNumber = package.LatestVersion ?? 0,
                PublicationMode = ReadEditorState(package.Graph).PublicationIntent.Mode,
                UpdatedAt = package.UpdatedAt
            })
            .ToArray();
    }

    public async Task<IReadOnlyList<StudioWorkflowNodeDefinition>> ListNodeDefinitionsAsync(
        CancellationToken cancellationToken = default)
    {
        var registry = await _api.GetNodeRegistryAsync(cancellationToken).ConfigureAwait(false);
        return registry.IsSuccess && registry.Data is not null
            ? MapNodeDefinitions(registry.Data)
            : [];
    }

    // A new workflow package is a local draft until the first save creates it on the server; this never
    // fabricates server-owned data (no package id, no version) and mirrors the editor "new" route.
    public Task<StudioWorkflowPackageDraft> CreateDraftAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(CreateLocalDraft());

    public async Task<StudioWorkflowPackageDraft?> GetDraftAsync(
        string draftId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(draftId);

        var registryTask = _api.GetNodeRegistryAsync(cancellationToken);
        var packageTask = _api.GetPackageAsync(draftId, cancellationToken);
        await Task.WhenAll(registryTask, packageTask).ConfigureAwait(false);

        var package = packageTask.Result;
        if (!package.IsSuccess || package.Data is null)
        {
            return null;
        }

        var definitions = registryTask.Result.IsSuccess && registryTask.Result.Data is not null
            ? MapNodeDefinitions(registryTask.Result.Data)
            : [];
        return ToDraft(package.Data, definitions);
    }

    public async Task<StudioWorkflowSaveResult> SaveVersionAsync(
        StudioWorkflowPackageDraft draft,
        string changeNote,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var (package, persistIssue) = await PersistPackageAsync(draft, cancellationToken).ConfigureAwait(false);
        if (package is null)
        {
            return new StudioWorkflowSaveResult { BindingState = ToBindingState(persistIssue) };
        }

        var version = await _api.CreateVersionAsync(package.PackageId, cancellationToken).ConfigureAwait(false);
        if (!version.IsSuccess || version.Data is null)
        {
            // PersistPackageAsync already PUT the (rejected) graph to the server's mutable package, so the
            // prior immutable version no longer matches the draft. Invalidate the version pin: leaving it set
            // would let EnsureVersionAsync (dry-run/publish) reuse a stale version and silently ship the old
            // graph. Cleared for any post-persist version-create failure; VersionNumber stays for display.
            draft.CurrentVersionId = string.Empty;

            // A 400 means the graph is structurally invalid - that is a validation outcome on a bound
            // surface, not a missing binding, so surface the server rules in-place rather than blocking.
            if (version.Issue?.StatusCode == 400)
            {
                var issues = new List<StudioWorkflowValidationIssue>
                {
                    new()
                    {
                        Severity = "error",
                        Scope = "graph",
                        Message = version.Issue.Detail
                    }
                };
                draft.ValidationIssues = issues;
                return new StudioWorkflowSaveResult
                {
                    ContentItemId = package.PackageId,
                    ValidationIssues = issues
                };
            }

            return new StudioWorkflowSaveResult { BindingState = ToBindingState(version.Issue) };
        }

        ApplyVersionToDraft(draft, version.Data);
        var validationIssues = MapValidation(version.Data.Validation);
        draft.ValidationIssues = validationIssues;

        return new StudioWorkflowSaveResult
        {
            ContentItemId = package.PackageId,
            VersionId = draft.CurrentVersionId,
            VersionNumber = version.Data.Version,
            PackageType = StudioWorkflowContractValues.PackageType,
            ContentItemType = StudioWorkflowContractValues.ContentItemType,
            Contract = $"workflow-package/{version.Data.SchemaVersion}",
            ValidationIssues = validationIssues
        };
    }

    public async Task<StudioWorkflowDryRunResult> DryRunAsync(
        StudioWorkflowPackageDraft draft,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var (handle, issue) = await EnsureVersionAsync(draft, cancellationToken).ConfigureAwait(false);
        if (handle is null)
        {
            return new StudioWorkflowDryRunResult { Status = "blocked", BindingState = ToBindingState(issue) };
        }

        var dryRun = await _api.DryRunVersionAsync(handle.PackageId, handle.Version, cancellationToken)
            .ConfigureAwait(false);
        if (!dryRun.IsSuccess || dryRun.Data is null)
        {
            return new StudioWorkflowDryRunResult { Status = "blocked", BindingState = ToBindingState(dryRun.Issue) };
        }

        return MapDryRun(dryRun.Data);
    }

    public async Task<StudioWorkflowPublishResult> PublishAsync(
        StudioWorkflowPackageDraft draft,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var (handle, ensureIssue) = await EnsureVersionAsync(draft, cancellationToken).ConfigureAwait(false);
        if (handle is null)
        {
            return new StudioWorkflowPublishResult { Status = "blocked", BindingState = ToBindingState(ensureIssue) };
        }

        var target = MapPublicationTarget(draft.PublicationIntent);
        var request = new PublishWorkflowPackageRequest
        {
            Target = target,
            ProcessId = target == WorkflowPublicationTarget.ProcessEndpoint
                ? NormalizeProcessId(draft.PublicationIntent.RouteSlug)
                : null,
            Enabled = true,
            Schedule = target == WorkflowPublicationTarget.Schedule ? ToSchedule(draft.Schedule) : null
        };

        var publication = await _api.PublishVersionAsync(handle.PackageId, handle.Version, request, cancellationToken)
            .ConfigureAwait(false);
        if (!publication.IsSuccess || publication.Data is null)
        {
            // A 400 is a publication-eligibility failure (blocked publish on a bound surface), not a missing
            // binding; surface the server eligibility rules in the publish panel.
            if (publication.Issue?.StatusCode is 400 or 409)
            {
                return new StudioWorkflowPublishResult
                {
                    Status = "blocked",
                    Mode = draft.PublicationIntent.Mode,
                    ValidationIssues =
                    [
                        new()
                        {
                            Severity = "error",
                            Scope = "publish",
                            Message = publication.Issue.Detail
                        }
                    ]
                };
            }

            return new StudioWorkflowPublishResult { Status = "blocked", BindingState = ToBindingState(publication.Issue) };
        }

        var result = MapPublication(draft, publication.Data);

        // Start the first run so job-backed publications link to real Operate evidence instead of
        // editor-local fake runs.
        var run = await _api.RunPublicationAsync(
                publication.Data.PublicationId,
                new RunWorkflowPublicationRequest { IdempotencyKey = $"console-publish-{publication.Data.PublicationId}" },
                cancellationToken)
            .ConfigureAwait(false);
        if (run.IsSuccess && run.Data is not null)
        {
            ApplyRunToPublishResult(result, run.Data);
        }
        else
        {
            // The publication exists on the server; only the immediate run could not start. Keep the
            // publication evidence and note the run gap rather than discarding a real publication.
            result.ValidationIssues =
            [
                .. result.ValidationIssues,
                new StudioWorkflowValidationIssue
                {
                    Severity = "warning",
                    Scope = "run",
                    Message = run.Issue?.Detail ?? "The publication was created but the first run could not start."
                }
            ];
        }

        return result;
    }

    // honua-server#1185 exposes no workflow job-evidence read; published-run job evidence lives in the Operate
    // surfaces (#60) via the OperateJobUrl on the publish result. Returning null keeps the contract honest.
    public Task<StudioWorkflowJobEvidence?> GetJobEvidenceAsync(
        string jobId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<StudioWorkflowJobEvidence?>(null);

    public async Task<StudioWorkflowRunHistory> ListRunHistoryAsync(
        string contentItemId,
        CancellationToken cancellationToken = default)
    {
        // A never-saved draft has no server content item; report an empty (bound) history so the panel shows
        // its "no runs yet" prompt instead of issuing a server call for an id that does not exist.
        if (string.IsNullOrWhiteSpace(contentItemId))
        {
            return StudioWorkflowRunHistory.Empty;
        }

        var publications = await _api.ListPublicationsAsync(cancellationToken).ConfigureAwait(false);
        if (!publications.IsSuccess || publications.Data is null)
        {
            return StudioWorkflowRunHistory.Blocked(ToBindingState(publications.Issue));
        }

        // honua-server#1185 lists publications, not per-execution runs; project each publication for this
        // package into a run-history row. Row-level (processed/rejected) counts and Operate job links are not
        // exposed on this contract yet, so leave them empty rather than fabricate them (#60 owns job evidence).
        var runs = publications.Data.Items
            .Where(publication => string.Equals(publication.PackageId, contentItemId, StringComparison.Ordinal))
            .OrderByDescending(publication => publication.CreatedAt)
            .Select(MapRun)
            .ToArray();

        return new StudioWorkflowRunHistory(runs);
    }

    public async Task<StudioWorkflowAiCapability> GetGenerationCapabilityAsync(
        CancellationToken cancellationToken = default)
    {
        var providers = await _api.ListGenerationProvidersAsync(cancellationToken).ConfigureAwait(false);
        if (!providers.IsSuccess || providers.Data is null)
        {
            // A 404/501 means this server has the workflow API but not the generation contract yet: AI is
            // simply off here (honest "unavailable"), not a missing server binding. Other issues (transport,
            // auth) block the surface so the page shows the shared blocked state.
            if (providers.Issue?.StatusCode is 404 or 501)
            {
                return StudioWorkflowAiCapability.Off;
            }

            return StudioWorkflowAiCapability.Blocked(ToBindingState(providers.Issue));
        }

        return new StudioWorkflowAiCapability
        {
            Enabled = providers.Data.Enabled,
            DefaultProvider = providers.Data.DefaultProvider,
            Providers = (providers.Data.Providers ?? [])
                .Select(provider => new StudioWorkflowAiProvider(
                    provider.Id,
                    string.IsNullOrWhiteSpace(provider.Label) ? provider.Id : provider.Label,
                    provider.Kind,
                    provider.Available,
                    provider.Detail))
                .ToArray()
        };
    }

    public async Task<StudioWorkflowGenerationOutcome> GenerateAsync(
        StudioWorkflowPackageDraft currentDraft,
        StudioWorkflowGenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(currentDraft);
        ArgumentNullException.ThrowIfNull(request);

        // A refine turn ships the current graph so the server edits it; a first turn ships null (fresh).
        var hasGraph = currentDraft.Nodes.Count > 0;
        var wire = new GenerateWorkflowRequest
        {
            Prompt = request.Prompt,
            Provider = request.Provider,
            Model = request.Model,
            Graph = hasGraph ? ToSaveRequest(currentDraft).Graph : null,
            Conversation = request.Conversation
                .Select(turn => new WorkflowGenerationTurn { Role = turn.Role, Content = turn.Content })
                .ToArray(),
            Answers = request.Answers
                .Select(answer => new WorkflowGenerationAnswer { QuestionId = answer.QuestionId, OptionId = answer.OptionId })
                .ToArray()
        };

        var result = await _api.GenerateWorkflowAsync(wire, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess || result.Data is null)
        {
            if (result.Issue?.StatusCode is 404 or 501)
            {
                return new StudioWorkflowGenerationOutcome
                {
                    Status = StudioWorkflowGenerationStatuses.Unsupported,
                    Rationale = "This server does not offer AI workflow generation yet."
                };
            }

            return StudioWorkflowGenerationOutcome.Blocked(ToBindingState(result.Issue));
        }

        // Only fetch the registry (for node category/ports/layout) when there is a graph to project.
        IReadOnlyList<StudioWorkflowNodeDefinition> definitions = [];
        if (result.Data.Graph is not null &&
            string.Equals(result.Data.Status, StudioWorkflowGenerationStatuses.Generated, StringComparison.Ordinal))
        {
            var registry = await _api.GetNodeRegistryAsync(cancellationToken).ConfigureAwait(false);
            definitions = registry.IsSuccess && registry.Data is not null
                ? MapNodeDefinitions(registry.Data)
                : [];
        }

        return MapGeneration(currentDraft, result.Data, definitions);
    }

    private StudioWorkflowGenerationOutcome MapGeneration(
        StudioWorkflowPackageDraft currentDraft,
        WorkflowGenerationResult result,
        IReadOnlyList<StudioWorkflowNodeDefinition> definitions)
    {
        // The server serializes empty collections as null (System.Text.Json source-gen omits empty arrays),
        // so every collection on the deserialized result may be null. Guard each before LINQ — otherwise a
        // perfectly valid 'generated' result (which carries no clarifications/unmapped) throws
        // ArgumentNullException here and terminates the Blazor circuit, freezing the page.
        var warnings = new List<string>();
        if (result.Validation is not null)
        {
            warnings.AddRange(result.Validation.Warnings ?? []);
        }

        warnings.AddRange((result.UnmappedRequests ?? [])
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => $"No matching step for: {item}"));

        var clarifications = (result.Clarifications ?? [])
            .Select(question => new StudioConversationClarification(
                question.Id,
                string.IsNullOrWhiteSpace(question.Prompt) ? question.Kind : question.Prompt,
                question.Reason ?? string.Empty,
                (question.Choices ?? [])
                    .Select(choice => new StudioConversationChoice(choice.Id, choice.Label, choice.Effect))
                    .ToArray()))
            .ToArray();

        StudioWorkflowPackageDraft? draft = null;
        var generated = string.Equals(result.Status, StudioWorkflowGenerationStatuses.Generated, StringComparison.Ordinal);
        if (generated && result.Graph is not null)
        {
            draft = ApplyGeneratedGraph(currentDraft, result.Graph, definitions);

            // Surface validator errors too (non-blocking here): the page re-validates authoritatively on save.
            if (result.Validation is { IsValid: false })
            {
                warnings.AddRange((result.Validation.Failures ?? []).Select(failure => failure.Message));
            }
        }

        return new StudioWorkflowGenerationOutcome
        {
            Status = NormalizeStatus(result.Status),
            Draft = draft,
            Rationale = result.Rationale ?? string.Empty,
            Clarifications = clarifications,
            Warnings = warnings,
            CapabilityState = result.CapabilityState is null
                ? null
                : new StudioWorkflowGenerationCapability(
                    result.CapabilityState.Name,
                    result.CapabilityState.State,
                    result.CapabilityState.Reason),
            Provider = result.Provider,
            Model = result.Model,
            FeedbackId = result.FeedbackId
        };
    }

    public async Task RecordGenerationFeedbackAsync(
        string feedbackId,
        string action,
        StudioWorkflowPackageDraft? finalDraft,
        string? note = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(feedbackId))
        {
            return;
        }

        // Best-effort: the operator's accept/edit/publish on a generated draft is the flywheel signal.
        // The final graph (when present) is the training target; the HTTP client never throws.
        await _api.RecordGenerationFeedbackAsync(
            new WorkflowGenerationFeedbackRequest
            {
                FeedbackId = feedbackId,
                Action = action,
                FinalGraph = finalDraft is { Nodes.Count: > 0 } ? ToSaveRequest(finalDraft).Graph : null,
                Note = note
            },
            cancellationToken).ConfigureAwait(false);
    }

    // Projects a server-proposed graph onto the live draft, reusing the same node/edge mapping as a reopened
    // package so the DAG preview reads identically. Identity (package/version ids, title) is preserved; the
    // version pin is cleared because the graph changed and must be re-saved + re-validated before any run.
    private StudioWorkflowPackageDraft ApplyGeneratedGraph(
        StudioWorkflowPackageDraft current,
        WorkflowGraph graph,
        IReadOnlyList<StudioWorkflowNodeDefinition> definitions)
    {
        var definitionsByType = definitions.ToDictionary(definition => definition.Type, StringComparer.Ordinal);
        var editorState = ReadEditorState(graph);

        current.Nodes = (graph.Nodes ?? [])
            .Select((node, index) => ToNode(node, index, definitionsByType))
            .ToList();
        current.Edges = (graph.Edges ?? []).Select(ToEdge).ToList();
        current.Schedule = ToSchedule(graph.Schedule, editorState.ScheduleMode);
        current.CurrentVersionId = string.Empty;

        // The server may suggest a package name in the graph editor metadata; adopt it only while the draft
        // still has its placeholder title, so a user rename is never clobbered by a refine.
        if (graph.EditorMetadata is not null &&
            graph.EditorMetadata.TryGetValue("console.title", out var suggested) &&
            !string.IsNullOrWhiteSpace(suggested) &&
            (string.IsNullOrWhiteSpace(current.Title) ||
             string.Equals(current.Title, "Untitled workflow package", StringComparison.Ordinal)))
        {
            current.Title = suggested;
        }

        return current;
    }

    private static string NormalizeStatus(string? status) => status switch
    {
        StudioWorkflowGenerationStatuses.Generated => StudioWorkflowGenerationStatuses.Generated,
        StudioWorkflowGenerationStatuses.NeedsClarification => StudioWorkflowGenerationStatuses.NeedsClarification,
        StudioWorkflowGenerationStatuses.Unsupported => StudioWorkflowGenerationStatuses.Unsupported,
        StudioWorkflowGenerationStatuses.Refused => StudioWorkflowGenerationStatuses.Refused,
        _ => StudioWorkflowGenerationStatuses.Error
    };

    private static StudioWorkflowRunHistoryEntry MapRun(WorkflowPublication publication) =>
        new()
        {
            RunId = publication.PublicationId,
            PublicationId = publication.PublicationId,
            ContentItemId = publication.PackageId,
            VersionId = FormatVersionId(publication.PackageId, publication.PackageVersion),
            JobId = string.Empty,
            JobKind = StudioWorkflowContractValues.PublicationJobKind,
            Status = publication.Status == WorkflowPublicationStatus.Active
                ? StudioWorkflowRunStatus.Succeeded
                : StudioWorkflowRunStatus.Failed,
            Mode = MapPublicationMode(publication.Target),
            Trigger = publication.Target == WorkflowPublicationTarget.Schedule ? "scheduled" : "publish",
            Logs = publication.Provenance
                .Select(entry => $"{entry.Key}: {entry.Value}")
                .ToArray(),
            ProvenanceUrl = $"/catalog/{publication.PackageId}",
            StartedAt = publication.CreatedAt
        };

    private static string MapPublicationMode(WorkflowPublicationTarget target) => target switch
    {
        WorkflowPublicationTarget.Schedule => StudioWorkflowContractValues.PublicationModeScheduledJob,
        WorkflowPublicationTarget.ProcessEndpoint => StudioWorkflowContractValues.PublicationModeProcessEndpoint,
        _ => StudioWorkflowContractValues.PublicationModeBatchWorkflow
    };

    private async Task<(WorkflowPackage? Package, WorkflowEndpointIssue? Issue)> PersistPackageAsync(
        StudioWorkflowPackageDraft draft,
        CancellationToken cancellationToken)
    {
        var request = ToSaveRequest(draft);
        var result = string.IsNullOrWhiteSpace(draft.ContentItemId)
            ? await _api.CreatePackageAsync(request, cancellationToken).ConfigureAwait(false)
            : await _api.UpdatePackageAsync(draft.ContentItemId, request, cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess || result.Data is null)
        {
            return (null, result.Issue);
        }

        draft.ContentItemId = result.Data.PackageId;
        draft.DraftId = result.Data.PackageId;
        draft.UpdatedAt = result.Data.UpdatedAt;
        return (result.Data, null);
    }

    // Reuses the saved immutable version when the draft already points at one; otherwise persists the draft
    // and creates a fresh version. Dry-run/publish operate on versions, so the editor saves dirty edits
    // first (page guard) and this reuses that snapshot - no redundant version churn per click.
    private async Task<(VersionHandle? Handle, WorkflowEndpointIssue? Issue)> EnsureVersionAsync(
        StudioWorkflowPackageDraft draft,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(draft.ContentItemId) &&
            TryParseVersion(draft.CurrentVersionId, out var existing))
        {
            return (new VersionHandle(draft.ContentItemId, existing), null);
        }

        var (package, persistIssue) = await PersistPackageAsync(draft, cancellationToken).ConfigureAwait(false);
        if (package is null)
        {
            return (null, persistIssue);
        }

        var version = await _api.CreateVersionAsync(package.PackageId, cancellationToken).ConfigureAwait(false);
        if (!version.IsSuccess || version.Data is null)
        {
            return (null, version.Issue);
        }

        ApplyVersionToDraft(draft, version.Data);
        draft.ValidationIssues = MapValidation(version.Data.Validation);
        return (new VersionHandle(package.PackageId, version.Data.Version), null);
    }

    private static void ApplyVersionToDraft(StudioWorkflowPackageDraft draft, WorkflowPackageVersion version)
    {
        draft.ContentItemId = version.PackageId;
        draft.DraftId = version.PackageId;
        draft.VersionNumber = version.Version;
        draft.CurrentVersionId = FormatVersionId(version.PackageId, version.Version);
        draft.SchemaVersion = version.SchemaVersion;
        draft.LastSavedAt = version.CreatedAt;
        draft.UpdatedAt = version.CreatedAt;
    }

    private static StudioWorkflowPackageDraft CreateLocalDraft()
    {
        var now = DateTimeOffset.UtcNow;
        return new StudioWorkflowPackageDraft
        {
            DraftId = string.Empty,
            ContentItemId = string.Empty,
            CurrentVersionId = string.Empty,
            VersionNumber = 0,
            PackageType = StudioWorkflowContractValues.PackageType,
            Title = "Untitled workflow package",
            Summary = "Author a GP/ETL workflow package and save a version to create it on the server.",
            Nodes = [],
            Edges = [],
            Parameters = [],
            OutputSchemas = [],
            Schedule = new StudioWorkflowSchedule { Mode = "manual" },
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    private static IReadOnlyList<StudioWorkflowNodeDefinition> MapNodeDefinitions(WorkflowNodeRegistrySnapshot snapshot) =>
        snapshot.Nodes
            .Select(node => new StudioWorkflowNodeDefinition
            {
                Type = node.NodeTypeId,
                Category = DeriveCategory(node),
                Label = node.Title,
                Summary = node.Description,
                InputPorts = node.InputSchemas.Select(port => port.Name).ToList(),
                OutputPorts = node.OutputSchemas.Select(port => port.Name).ToList()
            })
            .ToArray();

    // The editor groups the palette and lays out the graph by source/transform/sink. The server models node
    // shape via ports, so derive the bucket from port presence (no inputs => source, no outputs => sink).
    private static string DeriveCategory(WorkflowNodeDefinition node)
    {
        if (node.InputSchemas.Count == 0)
        {
            return StudioWorkflowContractValues.NodeCategorySource;
        }

        return node.OutputSchemas.Count == 0
            ? StudioWorkflowContractValues.NodeCategorySink
            : StudioWorkflowContractValues.NodeCategoryTransform;
    }

    private StudioWorkflowPackageDraft ToDraft(
        WorkflowPackage package,
        IReadOnlyList<StudioWorkflowNodeDefinition> definitions)
    {
        var definitionsByType = definitions.ToDictionary(definition => definition.Type, StringComparer.Ordinal);
        var editorState = ReadEditorState(package.Graph);

        var nodes = package.Graph.Nodes
            .Select((node, index) => ToNode(node, index, definitionsByType))
            .ToList();
        var edges = package.Graph.Edges.Select(ToEdge).ToList();

        return new StudioWorkflowPackageDraft
        {
            DraftId = package.PackageId,
            ContentItemId = package.PackageId,
            // Do NOT pin CurrentVersionId to LatestVersion on reopen. The server persists the mutable package
            // graph independently of immutable versions (a save whose POST .../versions failed graph
            // validation still PUT the graph but created no version), so a reopened package's graph can be
            // AHEAD of LatestVersion. Treating LatestVersion as the draft's current version would let a
            // dry-run/publish reuse a stale version whose graph differs from what is loaded (see
            // EnsureVersionAsync and the page's NeedsSaveBeforePublish gate, both keyed on CurrentVersionId).
            // Leaving it empty forces the loaded graph to be re-saved and re-validated before any run;
            // VersionNumber still carries the latest committed version for display.
            CurrentVersionId = string.Empty,
            VersionNumber = package.LatestVersion ?? 0,
            PackageType = StudioWorkflowContractValues.PackageType,
            SchemaVersion = package.Graph.SchemaVersion,
            Title = package.Name,
            Summary = package.Description ?? string.Empty,
            Nodes = nodes,
            Edges = edges,
            Parameters = editorState.Parameters,
            Schedule = ToSchedule(package.Graph.Schedule, editorState.ScheduleMode),
            WorkerProfile = ResolveWorkerProfile(package.Graph.WorkerProfile, editorState.WorkerProfile),
            RetryPolicy = editorState.RetryPolicy,
            PublicationIntent = editorState.PublicationIntent,
            OutputSchemas = editorState.OutputSchemas,
            CreatedAt = package.CreatedAt,
            UpdatedAt = package.UpdatedAt,
            LastSavedAt = package.LatestVersion.HasValue ? package.UpdatedAt : null
        };
    }

    private static StudioWorkflowNode ToNode(
        WorkflowNode node,
        int index,
        IReadOnlyDictionary<string, StudioWorkflowNodeDefinition> definitionsByType)
    {
        definitionsByType.TryGetValue(node.NodeTypeId, out var definition);

        // A freshly generated node carries null Metadata/Parameters (server omits empty maps); the
        // Dictionary copy-ctor and TryGetValue both throw on null, so normalize to empty first.
        var metadata = node.Metadata ?? EmptyStringMap;
        var parameters = node.Parameters ?? EmptyStringMap;

        return new StudioWorkflowNode
        {
            Id = node.NodeId,
            Type = node.NodeTypeId,
            Category = definition?.Category ?? StudioWorkflowContractValues.NodeCategoryTransform,
            Label = metadata.TryGetValue(NodeLabelKey, out var label) && !string.IsNullOrWhiteSpace(label)
                ? label
                : definition?.Label ?? node.NodeTypeId,
            Column = ReadInt(metadata, NodeColumnKey) ?? index + 1,
            Row = ReadInt(metadata, NodeRowKey) ?? 1,
            InputPorts = definition?.InputPorts.ToList() ?? [],
            OutputPorts = definition?.OutputPorts.ToList() ?? [],
            Configuration = new Dictionary<string, string>(parameters, StringComparer.Ordinal)
        };
    }

    private static StudioWorkflowEdge ToEdge(WorkflowEdge edge)
    {
        var kind = edge.Kind == WorkflowEdgeKind.Failure
            ? StudioWorkflowContractValues.EdgeKindFailure
            : StudioWorkflowContractValues.EdgeKindSuccess;

        return new StudioWorkflowEdge
        {
            Id = $"edge-{edge.SourceNodeId}-{edge.TargetNodeId}-{edge.SourcePort ?? "out"}",
            FromNodeId = edge.SourceNodeId,
            ToNodeId = edge.TargetNodeId,
            FromPort = edge.SourcePort ?? "out",
            ToPort = edge.TargetPort ?? "in",
            Kind = kind,
            Label = edge.SourcePort ?? kind
        };
    }

    private SaveWorkflowPackageRequest ToSaveRequest(StudioWorkflowPackageDraft draft)
    {
        var nodes = draft.Nodes
            .Select(node => new WorkflowNode
            {
                NodeId = node.Id,
                NodeTypeId = node.Type,
                Parameters = new Dictionary<string, string>(node.Configuration, StringComparer.Ordinal),
                Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [NodeLabelKey] = node.Label,
                    [NodeColumnKey] = node.Column.ToString(CultureInfo.InvariantCulture),
                    [NodeRowKey] = node.Row.ToString(CultureInfo.InvariantCulture)
                }
            })
            .ToList();

        var edges = draft.Edges
            .Select(edge => new WorkflowEdge
            {
                SourceNodeId = edge.FromNodeId,
                TargetNodeId = edge.ToNodeId,
                Kind = string.Equals(edge.Kind, StudioWorkflowContractValues.EdgeKindFailure, StringComparison.Ordinal)
                    ? WorkflowEdgeKind.Failure
                    : WorkflowEdgeKind.Data,
                SourcePort = edge.FromPort,
                TargetPort = edge.ToPort
            })
            .ToList();

        var editorMetadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [EditorStateKey] = SerializeEditorState(draft)
        };

        return new SaveWorkflowPackageRequest
        {
            PackageId = string.IsNullOrWhiteSpace(draft.ContentItemId) ? null : draft.ContentItemId,
            Name = string.IsNullOrWhiteSpace(draft.Title) ? "Untitled workflow package" : draft.Title,
            Description = string.IsNullOrWhiteSpace(draft.Summary) ? null : draft.Summary,
            Namespace = "studio.workflow",
            Graph = new WorkflowGraph
            {
                Nodes = nodes,
                Edges = edges,
                Schedule = ToSchedule(draft.Schedule),
                WorkerProfile = string.IsNullOrWhiteSpace(draft.WorkerProfile.ProfileId)
                    ? null
                    : draft.WorkerProfile.ProfileId,
                EditorMetadata = editorMetadata
            },
            Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["console.origin"] = "studio-workflow-editor"
            }
        };
    }

    private static StudioWorkflowDryRunResult MapDryRun(WorkflowDryRunResult dryRun)
    {
        var logs = new List<string>
        {
            string.Format(
                CultureInfo.InvariantCulture,
                "Estimated duration {0}s, relative cost weight {1:0.##}.",
                dryRun.EstimatedDurationSeconds,
                dryRun.EstimatedCostWeight)
        };
        logs.AddRange(dryRun.Logs.Select(entry =>
            string.IsNullOrWhiteSpace(entry.NodeId)
                ? $"[{entry.Level}] {entry.Message}"
                : $"[{entry.Level}] {entry.Message} ({entry.NodeId})"));
        logs.AddRange(dryRun.Validation.Failures.Select(failure => $"[validation] {failure.Message}"));

        return new StudioWorkflowDryRunResult
        {
            JobId = string.Empty,
            JobKind = StudioWorkflowContractValues.DryRunJobKind,
            Status = dryRun.Validation.IsValid ? "succeeded" : "blocked",
            SampleRows = 0,
            Logs = logs,
            Artifacts = dryRun.Artifacts.Select(artifact => $"{artifact.Kind}: {artifact.Label}").ToArray(),
            OutputSchemas = dryRun.OutputSchemas.Select(ToOutputSchema).ToArray(),
            // honua-server#1185 dry-run is a synchronous estimation that creates no Operate job, so no job
            // links here; published runs (below) are what land in Operate.
            OperateJobUrl = string.Empty,
            OperateEventsUrl = string.Empty
        };
    }

    private StudioWorkflowPublishResult MapPublication(StudioWorkflowPackageDraft draft, WorkflowPublication publication)
    {
        return new StudioWorkflowPublishResult
        {
            PublicationId = publication.PublicationId,
            ContentItemId = publication.PackageId,
            VersionId = FormatVersionId(publication.PackageId, publication.PackageVersion),
            JobId = string.Empty,
            JobKind = StudioWorkflowContractValues.PublicationJobKind,
            Status = publication.Status == WorkflowPublicationStatus.Active ? "published" : "disabled",
            Mode = draft.PublicationIntent.Mode,
            InvocationEndpoint = publication.Target == WorkflowPublicationTarget.ProcessEndpoint
                ? publication.EndpointPath
                : null,
            ValidationIssues = MapValidation(publication.Eligibility),
            OperateJobUrl = string.Empty,
            OperateEventsUrl = string.Empty
        };
    }

    private static void ApplyRunToPublishResult(StudioWorkflowPublishResult result, WorkflowPublicationRunResult run)
    {
        if (!string.IsNullOrWhiteSpace(run.JobId))
        {
            result.JobId = run.JobId;
            result.Status = "queued";
            result.OperateJobUrl = $"/operate/jobs/{run.JobId}";
            result.OperateEventsUrl = $"/operate/events?jobId={run.JobId}";
            return;
        }

        if (!string.IsNullOrWhiteSpace(run.WorkflowRunId))
        {
            result.Status = "queued";
            result.ValidationIssues =
            [
                .. result.ValidationIssues,
                new StudioWorkflowValidationIssue
                {
                    Severity = "warning",
                    Scope = "run",
                    Message = "The publication run was queued, but this workflow-run evidence does not expose an Operate job link yet."
                }
            ];
        }
    }

    private static StudioWorkflowOutputSchema ToOutputSchema(WorkflowNodePortSchema port)
    {
        var fields = port.Schema.Properties.Count > 0
            ? port.Schema.Properties
                .Select(property => new StudioWorkflowOutputField
                {
                    Name = property.Key,
                    Type = MapFieldType(property.Value),
                    Nullable = true
                })
                .ToList()
            :
            [
                new StudioWorkflowOutputField
                {
                    Name = string.IsNullOrWhiteSpace(port.Name) ? "value" : port.Name,
                    Type = MapFieldType(port.Schema),
                    Nullable = !port.Required
                }
            ];

        return new StudioWorkflowOutputSchema
        {
            Name = string.IsNullOrWhiteSpace(port.Title) ? port.Name : port.Title,
            SinkNodeId = string.Empty,
            Fields = fields
        };
    }

    private static string MapFieldType(WorkflowSchemaDefinition schema)
    {
        if (!string.IsNullOrWhiteSpace(schema.Format))
        {
            return schema.Format switch
            {
                "wkb" or "feature-layer" => "geometry",
                "table" => "table",
                "raster" => "raster",
                _ => schema.Format
            };
        }

        return schema.Type switch
        {
            WorkflowSchemaValueType.Text => "string",
            WorkflowSchemaValueType.WholeNumber => "integer",
            WorkflowSchemaValueType.DecimalNumber => "number",
            WorkflowSchemaValueType.Flag => "boolean",
            WorkflowSchemaValueType.List => "array",
            _ => "object"
        };
    }

    private static List<StudioWorkflowValidationIssue> MapValidation(WorkflowPackageValidationResult validation)
    {
        var issues = validation.Failures
            .Select(failure => new StudioWorkflowValidationIssue
            {
                Severity = "error",
                Scope = string.IsNullOrWhiteSpace(failure.FieldPath) ? failure.Code : failure.FieldPath!,
                Message = failure.Message
            })
            .ToList();

        issues.AddRange(validation.Warnings.Select(warning => new StudioWorkflowValidationIssue
        {
            Severity = "warning",
            Scope = "package",
            Message = warning
        }));

        return issues;
    }

    private static WorkflowPublicationTarget MapPublicationTarget(StudioWorkflowPublicationIntent intent)
    {
        if (intent.ExposeInvocationEndpoint ||
            string.Equals(intent.Mode, StudioWorkflowContractValues.PublicationModeProcessEndpoint, StringComparison.Ordinal))
        {
            return WorkflowPublicationTarget.ProcessEndpoint;
        }

        return intent.Mode switch
        {
            StudioWorkflowContractValues.PublicationModeScheduledJob => WorkflowPublicationTarget.Schedule,
            _ => WorkflowPublicationTarget.Job
        };
    }

    private static string? NormalizeProcessId(string? routeSlug) =>
        string.IsNullOrWhiteSpace(routeSlug) ? null : routeSlug.Trim();

    private static WorkflowSchedule? ToSchedule(StudioWorkflowSchedule schedule)
    {
        if (string.Equals(schedule.Mode, "manual", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(schedule.Cron))
        {
            return null;
        }

        return new WorkflowSchedule
        {
            CronExpression = schedule.Cron,
            TimeZone = string.IsNullOrWhiteSpace(schedule.TimeZone) ? null : schedule.TimeZone,
            Enabled = true
        };
    }

    private static StudioWorkflowSchedule ToSchedule(WorkflowSchedule? schedule, string editorMode)
    {
        if (schedule is null)
        {
            return new StudioWorkflowSchedule { Mode = "manual" };
        }

        return new StudioWorkflowSchedule
        {
            Mode = string.IsNullOrWhiteSpace(editorMode) || string.Equals(editorMode, "manual", StringComparison.OrdinalIgnoreCase)
                ? "cron"
                : editorMode,
            Cron = schedule.CronExpression,
            TimeZone = schedule.TimeZone ?? "UTC"
        };
    }

    private static StudioWorkflowWorkerProfile ResolveWorkerProfile(string? serverProfile, StudioWorkflowWorkerProfile editor)
    {
        if (!string.IsNullOrWhiteSpace(serverProfile))
        {
            editor.ProfileId = serverProfile;
        }

        return editor;
    }

    private string SerializeEditorState(StudioWorkflowPackageDraft draft)
    {
        var state = new WorkflowEditorState
        {
            Parameters = draft.Parameters,
            RetryPolicy = draft.RetryPolicy,
            PublicationIntent = draft.PublicationIntent,
            OutputSchemas = draft.OutputSchemas,
            ScheduleMode = draft.Schedule.Mode,
            WorkerProfile = draft.WorkerProfile
        };

        return JsonSerializer.Serialize(state, EditorStateOptions);
    }

    private WorkflowEditorState ReadEditorState(WorkflowGraph graph)
    {
        // EditorMetadata is null on a freshly generated graph (server omits the empty dict).
        if (graph.EditorMetadata is null ||
            !graph.EditorMetadata.TryGetValue(EditorStateKey, out var raw) ||
            string.IsNullOrWhiteSpace(raw))
        {
            return new WorkflowEditorState();
        }

        try
        {
            return JsonSerializer.Deserialize<WorkflowEditorState>(raw, EditorStateOptions) ?? new WorkflowEditorState();
        }
        catch (JsonException)
        {
            return new WorkflowEditorState();
        }
    }

    private static StudioWorkflowBindingState ToBindingState(WorkflowEndpointIssue? issue) =>
        issue is null
            ? new StudioWorkflowBindingState(
                Surface,
                "Unavailable",
                "honua-server workflow API",
                "The Honua server did not return workflow package data.")
            : new StudioWorkflowBindingState(Surface, issue.State, issue.Contract, issue.Detail);

    private static int? ReadInt(IReadOnlyDictionary<string, string> metadata, string key) =>
        metadata.TryGetValue(key, out var raw) &&
        int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    private static string FormatVersionId(string packageId, int version) =>
        $"{packageId}:v{version.ToString(CultureInfo.InvariantCulture)}";

    private static bool TryParseVersion(string versionId, out int version)
    {
        version = 0;
        if (string.IsNullOrWhiteSpace(versionId))
        {
            return false;
        }

        var marker = versionId.LastIndexOf(":v", StringComparison.Ordinal);
        return marker >= 0 &&
            int.TryParse(versionId[(marker + 2)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out version);
    }

    private sealed record VersionHandle(string PackageId, int Version);

    // Editor-only authoring fields the server graph does not model first-class. Persisted into the server's
    // opaque graph editor metadata so they round-trip without fabricating server-owned data.
    private sealed class WorkflowEditorState
    {
        public List<StudioWorkflowParameter> Parameters { get; set; } = [];
        public StudioWorkflowRetryPolicy RetryPolicy { get; set; } = new();
        public StudioWorkflowPublicationIntent PublicationIntent { get; set; } = new();
        public List<StudioWorkflowOutputSchema> OutputSchemas { get; set; } = [];
        public string ScheduleMode { get; set; } = "manual";
        public StudioWorkflowWorkerProfile WorkerProfile { get; set; } = new();
    }
}
