using System.Text.Json;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Transient in-memory placeholder for <see cref="IStudioWorkflowPackageClient"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Do not promote to a merged data source.</b> This client fakes the server-owned
/// workflow.package lifecycle so Console UX, unit tests, and the
/// <c>smoke/parity/workflow.mjs</c> harness can be built ahead of the server/SDK contract
/// (honua-console#40). Per <c>docs/migration/CONSOLE_PATTERNS_CHARTER.md</c> section 11 it
/// is a scaffold only: the workflow surface stays blocked until the real
/// <c>honua-server</c>/<c>honua-sdk-dotnet</c> projection lands, at which point this type is
/// replaced (not extended).
/// </para>
/// <para>
/// The implementation deliberately enforces explicit state-machine transition checks and
/// defensive input validation so a future server-backed replacement does not silently drop
/// a contract nuance Console relies on. See <see cref="IStudioWorkflowPackageClient"/> for
/// the enumerated invariants.
/// </para>
/// </remarks>
public sealed class InMemoryStudioWorkflowPackageClient : IStudioWorkflowPackageClient
{
    public const string SeedDraftId = "wf-draft-parcel-etl";

    private static readonly JsonSerializerOptions CloneOptions = new(JsonSerializerDefaults.Web);

    private readonly object _gate = new();
    private readonly Dictionary<string, StudioWorkflowPackageDraft> _drafts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, StudioWorkflowJobEvidence> _jobs = new(StringComparer.Ordinal);
    private readonly List<StudioWorkflowRunHistoryEntry> _runs = [];
    private readonly IReadOnlyList<StudioWorkflowNodeDefinition> _nodeDefinitions;
    private int _draftSequence = 1;
    private int _jobSequence;

    public InMemoryStudioWorkflowPackageClient(IEnumerable<StudioWorkflowPackageDraft>? seedDrafts = null)
    {
        _nodeDefinitions = CreateNodeDefinitions();

        foreach (var draft in seedDrafts ?? [CreateSeedDraft()])
        {
            _drafts[draft.DraftId] = Clone(draft);
        }
    }

    public static InMemoryStudioWorkflowPackageClient CreateSeeded() => new();

    public async Task<StudioWorkflowEditorContext> OpenEditorAsync(
        string? draftId,
        CancellationToken cancellationToken = default)
    {
        var nodeDefinitions = await ListNodeDefinitionsAsync(cancellationToken).ConfigureAwait(false);
        var draft = string.IsNullOrWhiteSpace(draftId) ||
            string.Equals(draftId, "new", StringComparison.OrdinalIgnoreCase)
            ? await CreateDraftAsync(cancellationToken).ConfigureAwait(false)
            : await GetDraftAsync(draftId, cancellationToken).ConfigureAwait(false);

        // The in-memory simulator is always "bound" to its seeded data, so it never carries a binding state.
        return new StudioWorkflowEditorContext(BindingState: null, nodeDefinitions, draft);
    }

    public Task<IReadOnlyList<StudioWorkflowDraftSummary>> ListDraftsAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<StudioWorkflowDraftSummary>>(
                _drafts.Values
                    .OrderByDescending(draft => draft.UpdatedAt)
                    .Select(draft => new StudioWorkflowDraftSummary
                    {
                        DraftId = draft.DraftId,
                        ContentItemId = draft.ContentItemId,
                        Title = draft.Title,
                        PackageType = draft.PackageType,
                        VersionNumber = draft.VersionNumber,
                        PublicationMode = draft.PublicationIntent.Mode,
                        UpdatedAt = draft.UpdatedAt
                    })
                    .ToArray());
        }
    }

    public Task<IReadOnlyList<StudioWorkflowNodeDefinition>> ListNodeDefinitionsAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(CloneList(_nodeDefinitions));
    }

    public Task<StudioWorkflowPackageDraft> CreateDraftAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var draft = CreateSeedDraft();
            _draftSequence += 1;
            draft.DraftId = $"wf-draft-new-{_draftSequence:000}";
            draft.ContentItemId = string.Empty;
            draft.CurrentVersionId = string.Empty;
            draft.VersionNumber = 0;
            draft.Title = "Untitled workflow package";
            draft.Summary = "Draft workflow package for GP and ETL authoring.";
            draft.CreatedAt = DateTimeOffset.UtcNow;
            draft.UpdatedAt = draft.CreatedAt;
            draft.LastSavedAt = null;
            draft.PublicationIntent.RouteSlug = $"workflow-{_draftSequence:000}";
            _drafts[draft.DraftId] = Clone(draft);
            return Task.FromResult(Clone(draft));
        }
    }

    public Task<StudioWorkflowPackageDraft?> GetDraftAsync(
        string draftId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(draftId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            return Task.FromResult(_drafts.TryGetValue(draftId, out var draft) ? Clone(draft) : null);
        }
    }

    public Task<StudioWorkflowSaveResult> SaveVersionAsync(
        StudioWorkflowPackageDraft draft,
        string changeNote,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentException.ThrowIfNullOrWhiteSpace(draft.DraftId);
        ArgumentException.ThrowIfNullOrWhiteSpace(changeNote);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            return Task.FromResult(SaveVersionLocked(draft, changeNote));
        }
    }

    public Task<StudioWorkflowDryRunResult> DryRunAsync(
        StudioWorkflowPackageDraft draft,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentException.ThrowIfNullOrWhiteSpace(draft.DraftId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var prepared = PrepareDraftForStorage(draft);
            var jobId = NextJobId("dryrun");
            var result = new StudioWorkflowDryRunResult
            {
                JobId = jobId,
                JobKind = StudioWorkflowContractValues.DryRunJobKind,
                Status = "succeeded",
                SampleRows = 48,
                Logs =
                [
                    $"Loaded {prepared.Nodes.Count} workflow nodes from {prepared.PackageType}.",
                    $"Validated {prepared.Parameters.Count} invocation parameters against sample data.",
                    "Materialized output schema and sample artifacts without publishing."
                ],
                Artifacts =
                [
                    $"{prepared.DraftId}/dry-run/sample-output.geojson",
                    $"{prepared.DraftId}/dry-run/output-schema.json",
                    $"{prepared.DraftId}/dry-run/execution-log.txt"
                ],
                OutputSchemas = CloneList(prepared.OutputSchemas),
                OperateJobUrl = $"/operate/jobs/{jobId}",
                OperateEventsUrl = $"/operate/events?jobId={jobId}"
            };

            _jobs[jobId] = new StudioWorkflowJobEvidence
            {
                JobId = jobId,
                JobKind = result.JobKind,
                Status = result.Status,
                DraftId = prepared.DraftId,
                ContentItemId = prepared.ContentItemId,
                VersionId = prepared.CurrentVersionId,
                Logs = result.Logs,
                Artifacts = result.Artifacts,
                OutputSchemas = result.OutputSchemas,
                OperateJobUrl = result.OperateJobUrl,
                OperateEventsUrl = result.OperateEventsUrl,
                CreatedAt = DateTimeOffset.UtcNow
            };

            RecordRun(prepared, jobId, result.JobKind, result.Status, "dry-run", result.Logs, result.Artifacts,
                processedRows: result.SampleRows, rejectedRows: 0, result.OperateJobUrl, result.OperateEventsUrl);

            return Task.FromResult(result);
        }
    }

    public Task<StudioWorkflowPublishResult> PublishAsync(
        StudioWorkflowPackageDraft draft,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentException.ThrowIfNullOrWhiteSpace(draft.DraftId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (RequiresVersionSave(draft))
            {
                _ = SaveVersionLocked(draft, "Saved before workflow publication.");
            }

            var prepared = PrepareDraftForStorage(draft);

            var parameterValidation = ValidateParameters(prepared).ToArray();
            var endpointRequested = IsEndpointRequested(prepared.PublicationIntent);
            if (HasPackageValidationErrors(prepared) ||
                (endpointRequested && parameterValidation.Any(validation => !validation.Valid)))
            {
                draft.ValidationIssues = CloneList(prepared.ValidationIssues).ToList();
                return Task.FromResult(CreateBlockedPublishResult(prepared, parameterValidation));
            }

            var jobId = NextJobId("publish");
            var publicationId = $"pub-{Slugify(prepared.Title)}-{prepared.VersionNumber:000}";
            var endpoint = endpointRequested
                ? $"/api/workspaces/{prepared.WorkspaceId}/workflows/{Slugify(prepared.PublicationIntent.RouteSlug)}/invoke"
                : null;

            var result = new StudioWorkflowPublishResult
            {
                PublicationId = publicationId,
                ContentItemId = prepared.ContentItemId,
                VersionId = prepared.CurrentVersionId,
                JobId = jobId,
                JobKind = StudioWorkflowContractValues.PublicationJobKind,
                Status = "queued",
                Mode = prepared.PublicationIntent.Mode,
                InvocationEndpoint = endpoint,
                ValidationIssues = CloneList(prepared.ValidationIssues),
                ParameterValidation = parameterValidation,
                OperateJobUrl = $"/operate/jobs/{jobId}",
                OperateEventsUrl = $"/operate/events?jobId={jobId}"
            };

            prepared.UpdatedAt = DateTimeOffset.UtcNow;
            _drafts[prepared.DraftId] = Clone(prepared);
            draft.UpdatedAt = prepared.UpdatedAt;
            draft.ValidationIssues = CloneList(prepared.ValidationIssues).ToList();

            _jobs[jobId] = new StudioWorkflowJobEvidence
            {
                JobId = jobId,
                JobKind = result.JobKind,
                Status = "queued",
                DraftId = prepared.DraftId,
                ContentItemId = prepared.ContentItemId,
                VersionId = prepared.CurrentVersionId,
                Logs =
                [
                    $"Queued publication {publicationId} for {prepared.PublicationIntent.Mode}.",
                    endpoint is null
                        ? "Invocation endpoint not requested for this publication."
                        : $"Invocation endpoint staged at {endpoint}.",
                    $"Operate event stream linked to {jobId}."
                ],
                Artifacts =
                [
                    $"{prepared.ContentItemId}/{prepared.CurrentVersionId}/workflow.package.json",
                    $"{prepared.ContentItemId}/{prepared.CurrentVersionId}/publication-review.json"
                ],
                OutputSchemas = CloneList(prepared.OutputSchemas),
                OperateJobUrl = result.OperateJobUrl,
                OperateEventsUrl = result.OperateEventsUrl,
                CreatedAt = DateTimeOffset.UtcNow
            };

            RecordRun(prepared, jobId, result.JobKind, result.Status,
                trigger: prepared.PublicationIntent.Mode == StudioWorkflowContractValues.PublicationModeScheduledJob
                    ? "scheduled"
                    : "publish",
                _jobs[jobId].Logs, _jobs[jobId].Artifacts, processedRows: 0, rejectedRows: 0,
                result.OperateJobUrl, result.OperateEventsUrl, publicationId);

            return Task.FromResult(result);
        }
    }

    public Task<StudioWorkflowRunHistory> ListRunHistoryAsync(
        string contentItemId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // A never-saved draft has no content item id yet; report an empty (bound) history rather than a
        // blocked surface so the editor's run-history panel renders its "no runs yet" prompt.
        if (string.IsNullOrWhiteSpace(contentItemId))
        {
            return Task.FromResult(StudioWorkflowRunHistory.Empty);
        }

        lock (_gate)
        {
            var runs = _runs
                .Where(run => string.Equals(run.ContentItemId, contentItemId, StringComparison.Ordinal))
                .OrderByDescending(run => run.StartedAt)
                .Select(Clone)
                .ToArray();
            return Task.FromResult(new StudioWorkflowRunHistory(runs));
        }
    }

    // The in-memory scaffold has no model backend and deliberately does NOT simulate one (real-deal phase:
    // no fabricated AI). It reports generation as off; the "Workflow from prompt" page renders its
    // AI-unavailable state. A live model is reached only through ServerStudioWorkflowPackageClient.
    public Task<StudioWorkflowAiCapability> GetGenerationCapabilityAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult(StudioWorkflowAiCapability.Off);

    public Task<StudioWorkflowGenerationOutcome> GenerateAsync(
        StudioWorkflowPackageDraft currentDraft,
        StudioWorkflowGenerationRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new StudioWorkflowGenerationOutcome
        {
            Status = StudioWorkflowGenerationStatuses.Unsupported,
            Rationale = "AI workflow generation requires a honua-server with the generation contract."
        });

    public Task RecordGenerationFeedbackAsync(
        string feedbackId,
        string action,
        StudioWorkflowPackageDraft? finalDraft,
        string? note = null,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    private void RecordRun(
        StudioWorkflowPackageDraft draft,
        string jobId,
        string jobKind,
        string status,
        string trigger,
        IReadOnlyList<string> logs,
        IReadOnlyList<string> artifacts,
        int processedRows,
        int rejectedRows,
        string operateJobUrl,
        string operateEventsUrl,
        string publicationId = "")
    {
        // Dry-runs report sample-row evidence but route no rejected rows; published runs that fan rows to a
        // failure sink report a representative rejected-row count so the panel surfaces row-level failures.
        var hasFailureSink = draft.Nodes.Any(node =>
            node.Type.Contains("failure", StringComparison.Ordinal));
        var effectiveRejected = rejectedRows > 0 || string.Equals(trigger, "dry-run", StringComparison.Ordinal)
            ? rejectedRows
            : hasFailureSink ? 3 : 0;

        _runs.Add(new StudioWorkflowRunHistoryEntry
        {
            RunId = $"run-{jobId}",
            PublicationId = publicationId,
            ContentItemId = draft.ContentItemId,
            VersionId = draft.CurrentVersionId,
            JobId = jobId,
            JobKind = jobKind,
            Status = status,
            Mode = draft.PublicationIntent.Mode,
            Trigger = trigger,
            ProcessedRows = processedRows,
            RejectedRows = effectiveRejected,
            Logs = logs.ToArray(),
            Artifacts = artifacts.ToArray(),
            OperateJobUrl = operateJobUrl,
            OperateEventsUrl = operateEventsUrl,
            ProvenanceUrl = string.IsNullOrWhiteSpace(draft.ContentItemId)
                ? string.Empty
                : $"/catalog/{draft.ContentItemId}",
            StartedAt = DateTimeOffset.UtcNow
        });
    }

    public Task<StudioWorkflowJobEvidence?> GetJobEvidenceAsync(
        string jobId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            return Task.FromResult(_jobs.TryGetValue(jobId, out var evidence) ? Clone(evidence) : null);
        }
    }

    private StudioWorkflowSaveResult SaveVersionLocked(StudioWorkflowPackageDraft draft, string changeNote)
    {
        var stored = PrepareDraftForStorage(draft);
        stored.ContentItemId = string.IsNullOrWhiteSpace(stored.ContentItemId)
            ? $"workflow-{Slugify(stored.Title)}"
            : stored.ContentItemId;

        var previousVersionNumber = _drafts.TryGetValue(stored.DraftId, out var existing)
            ? existing.VersionNumber
            : 0;
        stored.VersionNumber = Math.Max(stored.VersionNumber, previousVersionNumber) + 1;
        stored.CurrentVersionId = $"{stored.ContentItemId}:v{stored.VersionNumber}";
        stored.LastSavedAt = DateTimeOffset.UtcNow;
        stored.UpdatedAt = stored.LastSavedAt.Value;
        stored.ValidationIssues = ValidatePackage(stored);

        _drafts[stored.DraftId] = Clone(stored);

        draft.ContentItemId = stored.ContentItemId;
        draft.VersionNumber = stored.VersionNumber;
        draft.CurrentVersionId = stored.CurrentVersionId;
        draft.LastSavedAt = stored.LastSavedAt;
        draft.UpdatedAt = stored.UpdatedAt;
        draft.ValidationIssues = CloneList(stored.ValidationIssues).ToList();

        return new StudioWorkflowSaveResult
        {
            ContentItemId = stored.ContentItemId,
            VersionId = stored.CurrentVersionId,
            VersionNumber = stored.VersionNumber,
            PackageType = stored.PackageType,
            ContentItemType = StudioWorkflowContractValues.ContentItemType,
            Contract = "content-version/v1 + workflow.package/v1",
            ValidationIssues = CloneList(stored.ValidationIssues)
        };
    }

    private bool RequiresVersionSave(StudioWorkflowPackageDraft draft)
    {
        if (string.IsNullOrWhiteSpace(draft.CurrentVersionId))
        {
            return true;
        }

        if (!_drafts.TryGetValue(draft.DraftId, out var stored))
        {
            return true;
        }

        if (!string.Equals(draft.CurrentVersionId, stored.CurrentVersionId, StringComparison.Ordinal))
        {
            return true;
        }

        return !string.Equals(
            StudioWorkflowPackageDraftContent.CreateFingerprint(draft),
            StudioWorkflowPackageDraftContent.CreateFingerprint(stored),
            StringComparison.Ordinal);
    }

    private static StudioWorkflowPackageDraft PrepareDraftForStorage(StudioWorkflowPackageDraft draft)
    {
        var stored = Clone(draft);
        stored.PackageType = StudioWorkflowContractValues.PackageType;
        stored.SchemaVersion = StudioWorkflowContractValues.SchemaVersion;
        stored.UpdatedAt = DateTimeOffset.UtcNow;
        stored.ValidationIssues = ValidatePackage(stored);
        return stored;
    }

    private string NextJobId(string prefix)
    {
        _jobSequence += 1;
        return $"job-{prefix}-{_jobSequence:0000}";
    }

    private static List<StudioWorkflowValidationIssue> ValidatePackage(StudioWorkflowPackageDraft draft)
    {
        var issues = new List<StudioWorkflowValidationIssue>();

        if (!draft.Nodes.Any(node => node.Category == StudioWorkflowContractValues.NodeCategorySource))
        {
            issues.Add(new StudioWorkflowValidationIssue
            {
                Severity = "error",
                Scope = "graph",
                Message = "Workflow must include at least one source node."
            });
        }

        if (!draft.Nodes.Any(node => node.Category == StudioWorkflowContractValues.NodeCategoryTransform))
        {
            issues.Add(new StudioWorkflowValidationIssue
            {
                Severity = "error",
                Scope = "graph",
                Message = "Workflow must include at least one transform node."
            });
        }

        if (!draft.Nodes.Any(node => node.Category == StudioWorkflowContractValues.NodeCategorySink))
        {
            issues.Add(new StudioWorkflowValidationIssue
            {
                Severity = "error",
                Scope = "graph",
                Message = "Workflow must include at least one sink node."
            });
        }

        if (!draft.Edges.Any(edge => edge.Kind == StudioWorkflowContractValues.EdgeKindFailure))
        {
            issues.Add(new StudioWorkflowValidationIssue
            {
                Severity = "error",
                Scope = "failure-routing",
                Message = "Workflow must include at least one failure edge before publish."
            });
        }

        var nodeIds = draft.Nodes
            .Select(node => node.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var edge in draft.Edges)
        {
            if (!IsValidEdgeKind(edge.Kind))
            {
                issues.Add(new StudioWorkflowValidationIssue
                {
                    Severity = "error",
                    Scope = "graph",
                    Message = $"Edge '{(string.IsNullOrWhiteSpace(edge.Id) ? "(unnamed)" : edge.Id)}' declares an unsupported kind '{edge.Kind}'."
                });
            }

            if (!nodeIds.Contains(edge.FromNodeId) || !nodeIds.Contains(edge.ToNodeId))
            {
                issues.Add(new StudioWorkflowValidationIssue
                {
                    Severity = "error",
                    Scope = "graph",
                    Message = $"Edge '{(string.IsNullOrWhiteSpace(edge.Id) ? "(unnamed)" : edge.Id)}' references a node that is not part of the workflow graph."
                });
            }
        }

        if (!IsValidPublicationMode(draft.PublicationIntent.Mode))
        {
            issues.Add(new StudioWorkflowValidationIssue
            {
                Severity = "error",
                Scope = "publication",
                Message = $"Publication mode '{draft.PublicationIntent.Mode}' is not a supported workflow publication mode."
            });
        }

        if (draft.OutputSchemas.Count == 0)
        {
            issues.Add(new StudioWorkflowValidationIssue
            {
                Severity = "error",
                Scope = "output-schema",
                Message = "At least one output schema must be visible before publish."
            });
        }

        if (draft.PublicationIntent.Mode == StudioWorkflowContractValues.PublicationModeScheduledJob &&
            string.IsNullOrWhiteSpace(draft.Schedule.Cron))
        {
            issues.Add(new StudioWorkflowValidationIssue
            {
                Severity = "error",
                Scope = "schedule",
                Message = "Scheduled job publication requires a cron expression."
            });
        }

        issues.AddRange(ValidateParameters(draft)
            .Where(validation => !validation.Valid)
            .Select(validation => new StudioWorkflowValidationIssue
            {
                Severity = "error",
                Scope = "parameters",
                Message = validation.Message
            }));

        return issues;
    }

    private static IEnumerable<StudioWorkflowParameterValidation> ValidateParameters(StudioWorkflowPackageDraft draft)
    {
        var endpointRequested = IsEndpointRequested(draft.PublicationIntent);

        foreach (var parameter in draft.Parameters)
        {
            var validName = !string.IsNullOrWhiteSpace(parameter.Name);
            var validType = IsValidParameterType(parameter.Type);
            var valid = validName && validType;
            var message = valid
                ? endpointRequested
                    ? "Valid for invocation endpoint binding."
                    : "Valid for dry-run and job submission."
                : $"{(validName ? parameter.Name : "Unnamed parameter")} must declare a name and one of the supported parameter types.";

            yield return new StudioWorkflowParameterValidation
            {
                ParameterName = parameter.Name,
                Type = parameter.Type,
                Required = parameter.Required,
                Valid = valid,
                Message = message
            };
        }
    }

    private static bool IsEndpointRequested(StudioWorkflowPublicationIntent intent) =>
        intent.ExposeInvocationEndpoint ||
        intent.Mode == StudioWorkflowContractValues.PublicationModeProcessEndpoint;

    private static bool HasPackageValidationErrors(StudioWorkflowPackageDraft draft) =>
        draft.ValidationIssues.Any(issue =>
            string.Equals(issue.Severity, "error", StringComparison.OrdinalIgnoreCase));

    private static StudioWorkflowPublishResult CreateBlockedPublishResult(
        StudioWorkflowPackageDraft draft,
        IReadOnlyList<StudioWorkflowParameterValidation> parameterValidation) =>
        new()
        {
            ContentItemId = draft.ContentItemId,
            VersionId = draft.CurrentVersionId,
            JobKind = string.Empty,
            Mode = draft.PublicationIntent.Mode,
            Status = "blocked",
            ValidationIssues = CloneList(draft.ValidationIssues),
            ParameterValidation = parameterValidation
        };

    private static bool IsValidParameterType(string type) =>
        type is "string" or "date" or "number" or "boolean" or "geometry";

    private static bool IsValidEdgeKind(string kind) =>
        kind is StudioWorkflowContractValues.EdgeKindSuccess or StudioWorkflowContractValues.EdgeKindFailure;

    private static bool IsValidPublicationMode(string mode) =>
        mode is StudioWorkflowContractValues.PublicationModeBatchWorkflow
            or StudioWorkflowContractValues.PublicationModeScheduledJob
            or StudioWorkflowContractValues.PublicationModeProcessEndpoint;

    private static string Slugify(string value)
    {
        var chars = value
            .Trim()
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray();

        var compact = string.Join(
            '-',
            new string(chars)
                .Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        return string.IsNullOrWhiteSpace(compact) ? "workflow-package" : compact;
    }

    private static IReadOnlyList<StudioWorkflowNodeDefinition> CreateNodeDefinitions() =>
    [
        new()
        {
            Type = "honua.source.feature-layer",
            Category = StudioWorkflowContractValues.NodeCategorySource,
            Label = "Feature layer source",
            Summary = "Reads a server-owned feature layer or query result.",
            OutputPorts = ["features"]
        },
        new()
        {
            Type = "honua.transform.filter",
            Category = StudioWorkflowContractValues.NodeCategoryTransform,
            Label = "Filter rows",
            Summary = "Applies attribute, temporal, or spatial predicates.",
            InputPorts = ["features"],
            OutputPorts = ["matched", "rejected"]
        },
        new()
        {
            Type = "honua.transform.field-map",
            Category = StudioWorkflowContractValues.NodeCategoryTransform,
            Label = "Field mapper",
            Summary = "Renames, casts, and derives output fields.",
            InputPorts = ["features"],
            OutputPorts = ["features", "error"]
        },
        new()
        {
            Type = "honua.transform.gp-buffer",
            Category = StudioWorkflowContractValues.NodeCategoryTransform,
            Label = "GP buffer",
            Summary = "Runs a parameterized geoprocessing buffer operation.",
            InputPorts = ["features", "distance"],
            OutputPorts = ["features", "error"]
        },
        new()
        {
            Type = "honua.sink.feature-service",
            Category = StudioWorkflowContractValues.NodeCategorySink,
            Label = "Feature service sink",
            Summary = "Writes a versioned service output through server publication.",
            InputPorts = ["features"]
        },
        new()
        {
            Type = "honua.sink.failure-queue",
            Category = StudioWorkflowContractValues.NodeCategorySink,
            Label = "Failure queue",
            Summary = "Captures rejected rows, transform errors, and retry exhaustion.",
            InputPorts = ["error"]
        }
    ];

    private static StudioWorkflowPackageDraft CreateSeedDraft()
    {
        var now = DateTimeOffset.UtcNow;

        return new StudioWorkflowPackageDraft
        {
            DraftId = SeedDraftId,
            ContentItemId = "workflow-parcel-nightly-normalizer",
            CurrentVersionId = "workflow-parcel-nightly-normalizer:v1",
            VersionNumber = 1,
            Title = "Parcel nightly normalizer",
            Summary = "Normalize parcel attributes, route rejected records, and publish validated service output.",
            CreatedAt = now,
            UpdatedAt = now,
            LastSavedAt = now,
            Nodes =
            [
                new()
                {
                    Id = "source-parcels",
                    Type = "honua.source.feature-layer",
                    Category = StudioWorkflowContractValues.NodeCategorySource,
                    Label = "County parcels",
                    Column = 1,
                    Row = 1,
                    OutputPorts = ["features"],
                    Configuration = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["binding"] = "content-item:city-parcels-2026",
                        ["sample"] = "48 rows"
                    }
                },
                new()
                {
                    Id = "transform-filter",
                    Type = "honua.transform.filter",
                    Category = StudioWorkflowContractValues.NodeCategoryTransform,
                    Label = "Active parcel filter",
                    Column = 2,
                    Row = 1,
                    InputPorts = ["features"],
                    OutputPorts = ["matched", "rejected"],
                    Configuration = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["where"] = "status = 'active'",
                        ["spatialReference"] = "EPSG:2784"
                    }
                },
                new()
                {
                    Id = "transform-field-map",
                    Type = "honua.transform.field-map",
                    Category = StudioWorkflowContractValues.NodeCategoryTransform,
                    Label = "Normalize parcel fields",
                    Column = 3,
                    Row = 1,
                    InputPorts = ["features"],
                    OutputPorts = ["features", "error"],
                    Configuration = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["parcel_id"] = "apn",
                        ["owner_name"] = "owner"
                    }
                },
                new()
                {
                    Id = "sink-service",
                    Type = "honua.sink.feature-service",
                    Category = StudioWorkflowContractValues.NodeCategorySink,
                    Label = "Validated parcel service",
                    Column = 4,
                    Row = 1,
                    InputPorts = ["features"],
                    Configuration = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["serviceName"] = "parcel-normalized",
                        ["writeMode"] = "replace"
                    }
                },
                new()
                {
                    Id = "sink-failures",
                    Type = "honua.sink.failure-queue",
                    Category = StudioWorkflowContractValues.NodeCategorySink,
                    Label = "Rejected parcel records",
                    Column = 4,
                    Row = 2,
                    InputPorts = ["error"],
                    Configuration = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["retention"] = "30d",
                        ["notify"] = "operate"
                    }
                }
            ],
            Edges =
            [
                new()
                {
                    Id = "edge-source-filter",
                    FromNodeId = "source-parcels",
                    ToNodeId = "transform-filter",
                    FromPort = "features",
                    ToPort = "features",
                    Kind = StudioWorkflowContractValues.EdgeKindSuccess,
                    Label = "features"
                },
                new()
                {
                    Id = "edge-filter-map",
                    FromNodeId = "transform-filter",
                    ToNodeId = "transform-field-map",
                    FromPort = "matched",
                    ToPort = "features",
                    Kind = StudioWorkflowContractValues.EdgeKindSuccess,
                    Label = "matched"
                },
                new()
                {
                    Id = "edge-map-service",
                    FromNodeId = "transform-field-map",
                    ToNodeId = "sink-service",
                    FromPort = "features",
                    ToPort = "features",
                    Kind = StudioWorkflowContractValues.EdgeKindSuccess,
                    Label = "validated"
                },
                new()
                {
                    Id = "edge-filter-rejected",
                    FromNodeId = "transform-filter",
                    ToNodeId = "sink-failures",
                    FromPort = "rejected",
                    ToPort = "error",
                    Kind = StudioWorkflowContractValues.EdgeKindFailure,
                    Label = "rejected"
                },
                new()
                {
                    Id = "edge-map-errors",
                    FromNodeId = "transform-field-map",
                    ToNodeId = "sink-failures",
                    FromPort = "error",
                    ToPort = "error",
                    Kind = StudioWorkflowContractValues.EdgeKindFailure,
                    Label = "transform errors"
                }
            ],
            Parameters =
            [
                new()
                {
                    Name = "county",
                    Type = "string",
                    Required = true,
                    DefaultValue = "honolulu",
                    Description = "County code used by source binding and route naming."
                },
                new()
                {
                    Name = "effective_date",
                    Type = "date",
                    Required = true,
                    DefaultValue = "2026-05-24",
                    Description = "Effective date applied to temporal filters and output metadata."
                }
            ],
            Schedule = new StudioWorkflowSchedule
            {
                Mode = "cron",
                Cron = "0 6 * * *",
                TimeZone = "Pacific/Honolulu"
            },
            WorkerProfile = new StudioWorkflowWorkerProfile
            {
                ProfileId = "standard-geospatial",
                Runtime = "dotnet-gdal",
                Cpu = 4,
                MemoryGb = 16,
                MaxParallelism = 4
            },
            RetryPolicy = new StudioWorkflowRetryPolicy
            {
                MaxAttempts = 3,
                BackoffSeconds = 60,
                FailureMode = "route-failure-edges"
            },
            PublicationIntent = new StudioWorkflowPublicationIntent
            {
                Mode = StudioWorkflowContractValues.PublicationModeScheduledJob,
                Visibility = "org",
                RouteSlug = "parcel-nightly-normalizer",
                ExposeInvocationEndpoint = false,
                RequiresApproval = true
            },
            OutputSchemas =
            [
                new()
                {
                    Name = "validated_parcels",
                    SinkNodeId = "sink-service",
                    Fields =
                    [
                        new() { Name = "parcel_id", Type = "string", Nullable = false },
                        new() { Name = "owner_name", Type = "string", Nullable = true },
                        new() { Name = "zoning_code", Type = "string", Nullable = true },
                        new() { Name = "geometry", Type = "geometry", Nullable = false }
                    ]
                },
                new()
                {
                    Name = "rejected_parcels",
                    SinkNodeId = "sink-failures",
                    Fields =
                    [
                        new() { Name = "source_row_id", Type = "string", Nullable = false },
                        new() { Name = "failure_reason", Type = "string", Nullable = false },
                        new() { Name = "payload", Type = "json", Nullable = false }
                    ]
                }
            ],
            Warnings =
            [
                "Process endpoint eligibility requires every required parameter to have a supported scalar type.",
                "Failure edges will be preserved as server-owned job event routing metadata."
            ]
        };
    }

    private static T Clone<T>(T value)
    {
        var json = JsonSerializer.Serialize(value, CloneOptions);
        return JsonSerializer.Deserialize<T>(json, CloneOptions) ??
            throw new InvalidOperationException($"Could not clone {typeof(T).Name}.");
    }

    private static IReadOnlyList<T> CloneList<T>(IEnumerable<T> values) =>
        values.Select(Clone).ToArray();
}
