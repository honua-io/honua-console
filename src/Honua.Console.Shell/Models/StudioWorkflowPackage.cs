namespace Honua.Console.Shell.Models;

public static class StudioWorkflowContractValues
{
    public const string PackageType = "workflow.package";
    public const string SchemaVersion = "workflow.package/v1";
    public const string ContentItemType = "workflow";
    public const string DryRunJobKind = "workflow_dry_run";
    public const string PublicationJobKind = "batch_publication";

    public const string NodeCategorySource = "source";
    public const string NodeCategoryTransform = "transform";
    public const string NodeCategorySink = "sink";

    public const string EdgeKindSuccess = "success";
    public const string EdgeKindFailure = "failure";

    public const string PublicationModeBatchWorkflow = "batch-workflow";
    public const string PublicationModeScheduledJob = "scheduled-job";
    public const string PublicationModeProcessEndpoint = "process-endpoint";
}

public sealed class StudioWorkflowPackageDraft
{
    public string DraftId { get; set; } = string.Empty;
    public string ContentItemId { get; set; } = string.Empty;
    public string CurrentVersionId { get; set; } = string.Empty;
    public int VersionNumber { get; set; }
    public string PackageType { get; set; } = StudioWorkflowContractValues.PackageType;
    public string SchemaVersion { get; set; } = StudioWorkflowContractValues.SchemaVersion;
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string WorkspaceId { get; set; } = "workspace-honua-demo";
    public string Owner { get; set; } = "builder@honua.example";
    public List<StudioWorkflowNode> Nodes { get; set; } = [];
    public List<StudioWorkflowEdge> Edges { get; set; } = [];
    public List<StudioWorkflowParameter> Parameters { get; set; } = [];
    public StudioWorkflowSchedule Schedule { get; set; } = new();
    public StudioWorkflowWorkerProfile WorkerProfile { get; set; } = new();
    public StudioWorkflowRetryPolicy RetryPolicy { get; set; } = new();
    public StudioWorkflowPublicationIntent PublicationIntent { get; set; } = new();
    public List<StudioWorkflowOutputSchema> OutputSchemas { get; set; } = [];
    public List<StudioWorkflowValidationIssue> ValidationIssues { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastSavedAt { get; set; }
}

public sealed class StudioWorkflowDraftSummary
{
    public string DraftId { get; set; } = string.Empty;
    public string ContentItemId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string PackageType { get; set; } = StudioWorkflowContractValues.PackageType;
    public int VersionNumber { get; set; }
    public string PublicationMode { get; set; } = StudioWorkflowContractValues.PublicationModeBatchWorkflow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Missing-binding / unsupported / forbidden surface for the workflow editor. Mirrors
/// <c>OperateCapabilityState</c> and <c>StudioAuthoringBindingState</c>: when the workflow package data
/// cannot bind to a real honua-server (#1185), the editor renders this shared blocked surface (charter
/// §5/§11) instead of seeded workflow data.
/// </summary>
public sealed record StudioWorkflowBindingState(string Surface, string State, string Contract, string Detail);

/// <summary>
/// Result of opening the workflow editor for a draft. Carries the server node registry and the draft when
/// bound, or a <see cref="StudioWorkflowBindingState"/> when the surface is blocked - in a single call so
/// the page does not chain a separate binding probe.
/// </summary>
public sealed record StudioWorkflowEditorContext(
    StudioWorkflowBindingState? BindingState,
    IReadOnlyList<StudioWorkflowNodeDefinition> NodeDefinitions,
    StudioWorkflowPackageDraft? Draft);

public sealed class StudioWorkflowNodeDefinition
{
    public string Type { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public List<string> InputPorts { get; set; } = [];
    public List<string> OutputPorts { get; set; } = [];
}

public sealed class StudioWorkflowNode
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public int Column { get; set; }
    public int Row { get; set; }
    public List<string> InputPorts { get; set; } = [];
    public List<string> OutputPorts { get; set; } = [];
    public Dictionary<string, string> Configuration { get; set; } = new(StringComparer.Ordinal);
}

public sealed class StudioWorkflowEdge
{
    public string Id { get; set; } = string.Empty;
    public string FromNodeId { get; set; } = string.Empty;
    public string ToNodeId { get; set; } = string.Empty;
    public string FromPort { get; set; } = "out";
    public string ToPort { get; set; } = "in";
    public string Kind { get; set; } = StudioWorkflowContractValues.EdgeKindSuccess;
    public string Label { get; set; } = string.Empty;
}

public sealed class StudioWorkflowParameter
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "string";
    public bool Required { get; set; }
    public string DefaultValue { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public sealed class StudioWorkflowSchedule
{
    public string Mode { get; set; } = "manual";
    public string Cron { get; set; } = "0 6 * * *";
    public string TimeZone { get; set; } = "Pacific/Honolulu";
}

public sealed class StudioWorkflowWorkerProfile
{
    public string ProfileId { get; set; } = "standard-geospatial";
    public string Runtime { get; set; } = "dotnet-gdal";
    public int Cpu { get; set; } = 4;
    public int MemoryGb { get; set; } = 16;
    public int MaxParallelism { get; set; } = 4;
}

public sealed class StudioWorkflowRetryPolicy
{
    public int MaxAttempts { get; set; } = 3;
    public int BackoffSeconds { get; set; } = 60;
    public string FailureMode { get; set; } = "route-failure-edges";
}

public sealed class StudioWorkflowPublicationIntent
{
    public string Mode { get; set; } = StudioWorkflowContractValues.PublicationModeBatchWorkflow;
    public string Visibility { get; set; } = "private";
    public string RouteSlug { get; set; } = "parcel-nightly-normalizer";
    public bool ExposeInvocationEndpoint { get; set; }
    public bool RequiresApproval { get; set; } = true;
}

public sealed class StudioWorkflowOutputSchema
{
    public string Name { get; set; } = string.Empty;
    public string SinkNodeId { get; set; } = string.Empty;
    public List<StudioWorkflowOutputField> Fields { get; set; } = [];
}

public sealed class StudioWorkflowOutputField
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "string";
    public bool Nullable { get; set; }
}

public sealed class StudioWorkflowValidationIssue
{
    public string Severity { get; set; } = "warning";
    public string Scope { get; set; } = "package";
    public string Message { get; set; } = string.Empty;
}

public sealed class StudioWorkflowParameterValidation
{
    public string ParameterName { get; set; } = string.Empty;
    public string Type { get; set; } = "string";
    public bool Required { get; set; }
    public bool Valid { get; set; }
    public string Message { get; set; } = string.Empty;
}

public sealed class StudioWorkflowSaveResult
{
    public string ContentItemId { get; set; } = string.Empty;
    public string VersionId { get; set; } = string.Empty;
    public int VersionNumber { get; set; }
    public string PackageType { get; set; } = StudioWorkflowContractValues.PackageType;
    public string ContentItemType { get; set; } = StudioWorkflowContractValues.ContentItemType;
    public string Contract { get; set; } = "content-version/v1";
    public IReadOnlyList<StudioWorkflowValidationIssue> ValidationIssues { get; set; } = [];

    /// <summary>Set when the server-backed save could not bind; drives the shared blocked surface.</summary>
    public StudioWorkflowBindingState? BindingState { get; set; }
}

public sealed class StudioWorkflowDryRunResult
{
    public string JobId { get; set; } = string.Empty;
    public string JobKind { get; set; } = StudioWorkflowContractValues.DryRunJobKind;
    public string Status { get; set; } = "queued";
    public int SampleRows { get; set; }
    public IReadOnlyList<string> Logs { get; set; } = [];
    public IReadOnlyList<string> Artifacts { get; set; } = [];
    public IReadOnlyList<StudioWorkflowOutputSchema> OutputSchemas { get; set; } = [];
    public string OperateJobUrl { get; set; } = string.Empty;
    public string OperateEventsUrl { get; set; } = string.Empty;

    /// <summary>Set when the server-backed dry-run could not bind; drives the shared blocked surface.</summary>
    public StudioWorkflowBindingState? BindingState { get; set; }
}

public sealed class StudioWorkflowPublishResult
{
    public string PublicationId { get; set; } = string.Empty;
    public string ContentItemId { get; set; } = string.Empty;
    public string VersionId { get; set; } = string.Empty;
    public string JobId { get; set; } = string.Empty;
    public string JobKind { get; set; } = StudioWorkflowContractValues.PublicationJobKind;
    public string Status { get; set; } = "queued";
    public string Mode { get; set; } = StudioWorkflowContractValues.PublicationModeBatchWorkflow;
    public string? InvocationEndpoint { get; set; }
    public IReadOnlyList<StudioWorkflowValidationIssue> ValidationIssues { get; set; } = [];
    public IReadOnlyList<StudioWorkflowParameterValidation> ParameterValidation { get; set; } = [];
    public string OperateJobUrl { get; set; } = string.Empty;
    public string OperateEventsUrl { get; set; } = string.Empty;

    /// <summary>Set when the server-backed publish could not bind; drives the shared blocked surface.</summary>
    public StudioWorkflowBindingState? BindingState { get; set; }
}

public sealed class StudioWorkflowJobEvidence
{
    public string JobId { get; set; } = string.Empty;
    public string JobKind { get; set; } = string.Empty;
    public string Status { get; set; } = "queued";
    public string DraftId { get; set; } = string.Empty;
    public string ContentItemId { get; set; } = string.Empty;
    public string VersionId { get; set; } = string.Empty;
    public IReadOnlyList<string> Logs { get; set; } = [];
    public IReadOnlyList<string> Artifacts { get; set; } = [];
    public IReadOnlyList<StudioWorkflowOutputSchema> OutputSchemas { get; set; } = [];
    public string OperateJobUrl { get; set; } = string.Empty;
    public string OperateEventsUrl { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
