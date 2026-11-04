namespace Honua.Console.Shell.Models;

public static class StudioWorkflowRunStatus
{
    public const string Queued = "queued";
    public const string Running = "running";
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";

    public static bool IsFailure(string status) =>
        string.Equals(status, Failed, StringComparison.OrdinalIgnoreCase);

    public static bool IsInFlight(string status) =>
        string.Equals(status, Queued, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, Running, StringComparison.OrdinalIgnoreCase);
}

public sealed class StudioWorkflowRunHistoryEntry
{
    public string RunId { get; set; } = string.Empty;
    public string PublicationId { get; set; } = string.Empty;
    public string ContentItemId { get; set; } = string.Empty;
    public string VersionId { get; set; } = string.Empty;
    public string JobId { get; set; } = string.Empty;
    public string JobKind { get; set; } = string.Empty;

    public string Status { get; set; } = StudioWorkflowRunStatus.Queued;
    public string Mode { get; set; } = StudioWorkflowContractValues.PublicationModeBatchWorkflow;

    public string Trigger { get; set; } = "manual";
    public int ProcessedRows { get; set; }

    public int RejectedRows { get; set; }
    public IReadOnlyList<string> Logs { get; set; } = [];
    public IReadOnlyList<string> Artifacts { get; set; } = [];

    public string OperateJobUrl { get; set; } = string.Empty;
    public string OperateEventsUrl { get; set; } = string.Empty;

    public string ProvenanceUrl { get; set; } = string.Empty;
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed record StudioWorkflowRunHistory(
    IReadOnlyList<StudioWorkflowRunHistoryEntry> Runs,
    StudioWorkflowBindingState? BindingState = null)
{
    public static StudioWorkflowRunHistory Empty { get; } = new([], BindingState: null);

    public static StudioWorkflowRunHistory Blocked(StudioWorkflowBindingState bindingState) =>
        new([], bindingState);
}
