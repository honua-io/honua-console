using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Reads the Operate observability surface (server overview, events, alerts,
/// realtime rules, jobs, logs, investigations) from a real honua-server through
/// the <c>/api/v1/admin/...</c> contracts. Each section is independently
/// permissioned, so every read returns an <see cref="OperateSectionResult{T}"/>
/// carrying a status that drives the shared empty/forbidden/unsupported/
/// unavailable surfaces, mirroring the catalog client's read envelope.
/// </summary>
public interface IConsoleOperateObservabilityClient
{
    Task<OperateSectionResult<OperateFleetOverview>> GetOverviewAsync(
        CancellationToken cancellationToken = default);

    Task<OperateSectionResult<IReadOnlyList<OperateEventRow>>> QueryEventsAsync(
        OperateEventQuery query,
        CancellationToken cancellationToken = default);

    Task<OperateSectionResult<OperateLogsView>> GetLogsAsync(
        CancellationToken cancellationToken = default);

    Task<OperateSectionResult<IReadOnlyList<OperateAlertRecord>>> GetAlertsAsync(
        CancellationToken cancellationToken = default);

    Task<OperateSectionResult<OperateRulesView>> GetRulesAsync(
        CancellationToken cancellationToken = default);

    Task<OperateSectionResult<IReadOnlyList<OperateJobRun>>> GetJobsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the durable job list filtered to a single execution job kind
    /// (<c>GET /api/v1/admin/jobs?kind=…</c>), so a kind-scoped dashboard (e.g.
    /// the Geoprocessing jobs surface) does not pull the whole fleet job page and
    /// filter client-side. <paramref name="kind"/> is the wire enum name the
    /// server's <c>kind</c> query parser accepts (e.g. <c>Geoprocessing</c>);
    /// a null/blank value loads the unfiltered list like
    /// <see cref="GetJobsAsync(CancellationToken)"/>.
    /// </summary>
    Task<OperateSectionResult<IReadOnlyList<OperateJobRun>>> GetJobsAsync(
        string? kind,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Convenience over <see cref="GetJobsAsync(string?, CancellationToken)"/>
    /// scoped to the <c>Geoprocessing</c> execution job kind, backing the
    /// Operate &gt; Geoprocessing jobs dashboard.
    /// </summary>
    Task<OperateSectionResult<IReadOnlyList<OperateJobRun>>> GetGeoprocessingJobsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads a single job's stages, logs, artifacts, and server-declared
    /// actions on demand (the list endpoint omits these), so the jobs viewer
    /// does not fan out a detail/log/artifact request per row up front.
    /// </summary>
    Task<OperateSectionResult<OperateJobRun>> GetJobDetailAsync(
        string jobRunId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels a non-terminal durable job through the gated control endpoint
    /// (<c>POST /api/v1/admin/jobs/{id}/cancel</c>). The server enforces the
    /// Execute + destructive-approval gate; a denied cancel surfaces here as a
    /// <see cref="OperateSectionStatus.Forbidden"/> result (carrying the server's
    /// "approval required" message when the gate asks for approval), and a job
    /// that is no longer cancellable surfaces as a conflict
    /// (<see cref="OperateSectionStatus.Unavailable"/>). The console never
    /// bypasses the gate; it only invokes the action and surfaces the result.
    /// </summary>
    Task<OperateSectionResult<OperateJobControlOutcome>> CancelJobAsync(
        string jobRunId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Requeues a Failed or Cancelled durable job through the gated control
    /// endpoint (<c>POST /api/v1/admin/jobs/{id}/retry</c>). Same gate/result
    /// surfacing as <see cref="CancelJobAsync"/>; the server returns 409 (mapped
    /// to a conflict result) when the job is not in a retryable state.
    /// </summary>
    Task<OperateSectionResult<OperateJobControlOutcome>> RetryJobAsync(
        string jobRunId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a single job's sanitized per-step glass-box
    /// (<c>GET /api/v1/admin/jobs/{id}/steps</c>, honua-server #2182): the ordered
    /// steps with phase, status, timeline/duration, the server-sanitized provider
    /// command, the per-step artifacts, and metadata. Loaded lazily by the job
    /// detail surface (the detail endpoint omits per-step depth). The command is
    /// already sanitized server-side; the Console renders it verbatim.
    /// </summary>
    Task<OperateSectionResult<OperateJobStepsView>> GetJobStepsAsync(
        string jobRunId,
        CancellationToken cancellationToken = default);

    Task<OperateSectionResult<IReadOnlyList<OperateInvestigation>>> GetInvestigationsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the connected server's recent-error buffer
    /// (<c>GET /api/v1/admin/observability/errors</c>) so callers can attach live
    /// error context (e.g. the in-product support ticket loop) without forcing the
    /// customer to copy log dumps. Returns the raw buffer rows on success.
    /// </summary>
    Task<OperateSectionResult<IReadOnlyList<OperateRecentError>>> GetRecentErrorsAsync(
        int limit = 10,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// A single recent-error buffer row projected from the server admin
/// observability errors endpoint, surfaced for context attachment.
/// </summary>
public sealed record OperateRecentError(
    DateTimeOffset Timestamp,
    string CorrelationId,
    string Path,
    int StatusCode,
    string Message);

public enum OperateSectionStatus
{
    Allowed,
    Missing,
    Forbidden,
    Rejected,
    Conflict,
    Unavailable,
    Unsupported
}

/// <summary>
/// Consistent titles and fallback copy for the non-allowed section states, so
/// missing/forbidden/unsupported/unavailable render through one shared surface
/// (per the Console exception-handling constraint).
/// </summary>
public static class OperateSectionPresentation
{
    public static string Title(OperateSectionStatus status) => status switch
    {
        OperateSectionStatus.Missing => "Not found",
        OperateSectionStatus.Forbidden => "Permission required",
        OperateSectionStatus.Unsupported => "Unsupported by this server",
        _ => "Temporarily unavailable"
    };

    public static string FallbackMessage(OperateSectionStatus status) => status switch
    {
        OperateSectionStatus.Missing => "This server build does not expose this Operate surface.",
        OperateSectionStatus.Forbidden => "The active environment profile is not permitted to read this surface.",
        OperateSectionStatus.Unsupported => "The connected server does not advertise this capability.",
        _ => "The honua-server admin API could not be reached. Retry once the environment is connected."
    };
}

public sealed record OperateFleetOverview(
    IReadOnlyList<OperateEnvironmentOverview> Environments,
    IReadOnlyList<OperateTelemetryFact> TelemetryFacts,
    IReadOnlyList<OperateCompatibilityRow> CompatibilityRows)
{
    public static OperateFleetOverview Empty { get; } = new([], [], []);
}

public sealed record OperateRulesView(
    IReadOnlyList<OperateAlertRule> Rules,
    IReadOnlyList<OperateGeofenceZone> Zones,
    OperateSectionStatus ZonesStatus = OperateSectionStatus.Allowed,
    string ZonesMessage = "")
{
    public static OperateRulesView Empty { get; } = new([], []);

    public bool ZonesAllowed => ZonesStatus == OperateSectionStatus.Allowed;
}

public sealed record OperateLogsView(
    string InstanceId,
    int Capacity,
    IReadOnlyList<OperateLogRecord> Logs,
    IReadOnlyList<OperateLogGroup> SeverityBuckets,
    IReadOnlyList<OperateLogGroup> ExceptionGroups)
{
    public static OperateLogsView Empty { get; } = new(string.Empty, 0, [], [], []);
}

public sealed record OperateLogRecord(
    string Timestamp,
    string Level,
    OperateStatus Severity,
    string Path,
    int StatusCode,
    string CorrelationId,
    string Message,
    IReadOnlyList<OperateEvidenceLink> ProviderLinks);

public sealed record OperateLogGroup(string Label, int Count, OperateStatus Status);

public sealed record OperateEventQuery
{
    public string EventType { get; init; } = string.Empty;

    public string CorrelationId { get; init; } = string.Empty;

    public string MinSeverity { get; init; } = string.Empty;

    public string EnvironmentId { get; init; } = string.Empty;

    public string TraceId { get; init; } = string.Empty;

    public string RequestId { get; init; } = string.Empty;

    public string ServiceId { get; init; } = string.Empty;

    public string ResourceRef { get; init; } = string.Empty;

    public string Actor { get; init; } = string.Empty;

    public string OperationId { get; init; } = string.Empty;

    public string ReleaseId { get; init; } = string.Empty;

    public string ChangeSetId { get; init; } = string.Empty;

    public string From { get; init; } = string.Empty;

    public string To { get; init; } = string.Empty;

    public static OperateEventQuery Empty { get; } = new();
}

public sealed record OperateSectionResult<T>
{
    public OperateSectionStatus Status { get; init; }

    public T? Value { get; init; }

    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// Verbatim technical diagnostics (transport status, contract) relocated out of the
    /// human-facing <see cref="Message"/> for the shared diagnostics disclosure (honua-console#311).
    /// Null when the message needs no technical layer.
    /// </summary>
    public string? Detail { get; init; }

    /// <summary>
    /// True when the server returned a partial result (e.g. one event source
    /// was unreachable). The data is still live; the surface should annotate it.
    /// </summary>
    public bool PartialResult { get; init; }

    public bool IsAllowed => Status == OperateSectionStatus.Allowed;

    public static OperateSectionResult<T> Allowed(T value, bool partialResult = false, string message = "") =>
        new()
        {
            Status = OperateSectionStatus.Allowed,
            Value = value,
            PartialResult = partialResult,
            Message = message
        };

    public static OperateSectionResult<T> Denied(OperateSectionStatus status, string message, string? detail = null) =>
        new()
        {
            Status = status,
            Message = message,
            Detail = detail
        };
}
