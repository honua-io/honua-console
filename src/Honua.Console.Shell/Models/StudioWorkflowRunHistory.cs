namespace Honua.Console.Shell.Models;

/// <summary>
/// Run-state vocabulary for workflow executions surfaced in the editor's run-history panel. Preserved across
/// Studio and Operate surfaces: queued/running are neutral in-flight states, succeeded is terminal-ok, and
/// failed renders through the shared failure surface. Kept alongside (not inside)
/// <see cref="StudioWorkflowContractValues"/> so the run-history slice is self-contained.
/// </summary>
public static class StudioWorkflowRunStatus
{
    public const string Queued = "queued";
    public const string Running = "running";
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";

    /// <summary>True when the run has reached a terminal failure and should render through the failure surface.</summary>
    public static bool IsFailure(string status) =>
        string.Equals(status, Failed, StringComparison.OrdinalIgnoreCase);

    /// <summary>True when the run is still in flight (queued or running) and may transition further.</summary>
    public static bool IsInFlight(string status) =>
        string.Equals(status, Queued, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, Running, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// One workflow execution surfaced in the editor's run-history panel: the publication/run identity, its
/// queued/running/succeeded/failed state, the rejected-row count, and links into Operate job/event evidence
/// and the content-item provenance. Sourced from server publications and their runs (or seeded dry-run and
/// publish jobs in the in-memory simulator) - never fabricated success on an unbound surface.
/// </summary>
public sealed class StudioWorkflowRunHistoryEntry
{
    public string RunId { get; set; } = string.Empty;
    public string PublicationId { get; set; } = string.Empty;
    public string ContentItemId { get; set; } = string.Empty;
    public string VersionId { get; set; } = string.Empty;
    public string JobId { get; set; } = string.Empty;
    public string JobKind { get; set; } = string.Empty;

    /// <summary>One of the <see cref="StudioWorkflowRunStatus"/> values.</summary>
    public string Status { get; set; } = StudioWorkflowRunStatus.Queued;
    public string Mode { get; set; } = StudioWorkflowContractValues.PublicationModeBatchWorkflow;

    /// <summary>Trigger that started the run (e.g. <c>dry-run</c>, <c>publish</c>, <c>scheduled</c>, <c>manual</c>).</summary>
    public string Trigger { get; set; } = "manual";
    public int ProcessedRows { get; set; }

    /// <summary>Rejected/quarantined rows routed to a failure sink; surfaces row-level failures in the editor.</summary>
    public int RejectedRows { get; set; }
    public IReadOnlyList<string> Logs { get; set; } = [];
    public IReadOnlyList<string> Artifacts { get; set; } = [];

    /// <summary>Job-scoped Operate evidence URL, or empty when the run produced no Operate job.</summary>
    public string OperateJobUrl { get; set; } = string.Empty;
    public string OperateEventsUrl { get; set; } = string.Empty;

    /// <summary>Content-item provenance link (versions / rollback), or empty when not yet a saved content item.</summary>
    public string ProvenanceUrl { get; set; } = string.Empty;
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Run-history projection for a workflow draft: the ordered run entries (newest first) plus an optional
/// <see cref="StudioWorkflowBindingState"/> when the surface is blocked / unbound. Mirrors the other workflow
/// result types so the editor renders the shared blocked surface instead of empty/fabricated history.
/// </summary>
public sealed record StudioWorkflowRunHistory(
    IReadOnlyList<StudioWorkflowRunHistoryEntry> Runs,
    StudioWorkflowBindingState? BindingState = null)
{
    public static StudioWorkflowRunHistory Empty { get; } = new([], BindingState: null);

    public static StudioWorkflowRunHistory Blocked(StudioWorkflowBindingState bindingState) =>
        new([], bindingState);
}
