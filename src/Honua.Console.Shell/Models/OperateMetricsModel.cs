using Honua.Console.Shell.Services;

namespace Honua.Console.Shell.Models;

public static class OperateMetricsRoutes
{
    public const string Root = "/operate";
    public const string Metrics = "/operate/metrics";
}

/// <summary>
/// One metrics sub-read carrying its own section status so the page degrades each
/// metric independently (one unavailable metric must not blank the whole surface).
/// </summary>
public sealed record OperateMetricSection<T>(
    OperateSectionStatus Status,
    T? Value,
    string Message)
{
    public bool IsAllowed => Status == OperateSectionStatus.Allowed && Value is not null;

    public static OperateMetricSection<T> Allowed(T value) =>
        new(OperateSectionStatus.Allowed, value, string.Empty);

    public static OperateMetricSection<T> Denied(OperateSectionStatus status, string message) =>
        new(status, default, message);

    public static OperateMetricSection<T> From(OperateSectionResult<T> result) =>
        result.IsAllowed
            ? new(OperateSectionStatus.Allowed, result.Value, result.Message)
            : new(result.Status, default, result.Message);
}

/// <summary>
/// A scannable utilization/ratio bar: a width-percentage plus the raw display value
/// and a color-coded status, so the page renders a CSS bar (no charting library).
/// </summary>
public sealed record OperateMetricBar(
    double Percent,
    string DisplayValue,
    OperateStatus Status,
    bool HasData)
{
    public int ClampedWidth => (int)Math.Round(Math.Clamp(Percent, 0, 100));
}

public sealed record OperateConnectionPoolMetric(
    OperateMetricBar Utilization,
    OperateStatus Health,
    string HealthLabel,
    long TotalFailures,
    long TotalTimeouts,
    OperateQueryAdmissionMetric? QueryAdmission,
    string Timestamp);

public sealed record OperateQueryAdmissionMetric(
    bool AdaptiveEnabled,
    int CurrentLimit,
    int MinLimit,
    int MaxLimit,
    int InFlight,
    int AvailableSlots,
    int QueuedWaiters,
    double TargetDurationMs,
    double DurationEwmaMs,
    double QueueWaitEwmaMs,
    long AdjustmentCount,
    string LastAdjustmentDirection,
    OperateMetricBar SlotUtilization);

public sealed record OperateCacheMetric(
    OperateMetricBar HitRatio,
    OperateStatus Health,
    IReadOnlyList<string> Recommendations,
    string Timestamp);

public sealed record OperateResourceMetric(
    long MemoryUsageMB,
    long MemoryUsageBytes,
    string MemoryPressureLevel,
    OperateStatus PressureStatus,
    OperateStatus Health,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections,
    long GcTotalMemoryBytes,
    string Timestamp);

public sealed record OperateUploadQueueMetric(
    OperateMetricBar QueueDepth,
    int QueueDepthValue,
    int MaxQueueDepth,
    int ActiveUploads,
    int MaxConcurrentUploads,
    OperateStatus Health,
    string Timestamp);

public sealed record OperateDatabaseResilienceMetric(
    OperateMetricBar ErrorRate,
    bool CircuitBreakerEnabled,
    long ConnectionFailures,
    long QueryFailures,
    OperateMetricBar PoolUtilization,
    OperateStatus PoolHealth,
    string PoolHealthLabel,
    IReadOnlyList<string> Alerts,
    string Timestamp);

public sealed record OperateHealthCheckEntry(
    string Name,
    OperateStatus Status,
    string StatusLabel,
    string DurationLabel,
    string Description);

public sealed record OperateComprehensiveHealthMetric(
    OperateStatus Status,
    string StatusLabel,
    string TotalDurationLabel,
    IReadOnlyList<OperateHealthCheckEntry> Entries,
    string Timestamp);

/// <summary>
/// Aggregated metrics snapshot: each section carries its own status so the page
/// renders live values where bound and an honest missing-binding state per metric.
/// </summary>
public sealed record OperateMetricsSnapshot(
    OperateMetricSection<OperateConnectionPoolMetric> ConnectionPool,
    OperateMetricSection<OperateCacheMetric> Cache,
    OperateMetricSection<OperateResourceMetric> Resources,
    OperateMetricSection<OperateUploadQueueMetric> UploadQueue,
    OperateMetricSection<OperateDatabaseResilienceMetric> DatabaseResilience,
    OperateMetricSection<OperateComprehensiveHealthMetric> Health)
{
    /// <summary>
    /// True when every sub-read was denied with the same not-bound state — used by
    /// the page to show one top-level missing-binding banner instead of six.
    /// </summary>
    public bool IsEntirelyUnbound =>
        !ConnectionPool.IsAllowed
        && !Cache.IsAllowed
        && !Resources.IsAllowed
        && !UploadQueue.IsAllowed
        && !DatabaseResilience.IsAllowed
        && !Health.IsAllowed;
}
