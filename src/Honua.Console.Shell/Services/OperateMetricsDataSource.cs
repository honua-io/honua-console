using System.Globalization;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Loads the production-monitoring metrics in parallel into a single
/// <see cref="OperateMetricsSnapshot"/>, mapping each server wire response into the
/// color-coded Operate view models. Each sub-read degrades independently (one
/// unavailable metric does not blank the page), and a not-bound server yields an
/// honest missing-binding state per section — never fabricated metrics
/// (Console Patterns Charter section 11).
/// </summary>
public interface IOperateMetricsDataSource
{
    Task<OperateMetricsSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);
}

public sealed class OperateMetricsDataSource : IOperateMetricsDataSource
{
    private readonly IConsoleMonitoringMetricsClient _client;

    public OperateMetricsDataSource(IConsoleMonitoringMetricsClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public async Task<OperateMetricsSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        // Load every independently-permissioned metric in parallel rather than through
        // a blocking aggregate, protecting the Operate path from a request waterfall.
        var poolTask = _client.GetConnectionPoolMetricsAsync(cancellationToken);
        var cacheTask = _client.GetCacheMetricsAsync(cancellationToken);
        var resourceTask = _client.GetResourceMetricsAsync(cancellationToken);
        var uploadTask = _client.GetUploadQueueMetricsAsync(cancellationToken);
        var dbTask = _client.GetDatabaseResilienceMetricsAsync(cancellationToken);
        var healthTask = _client.GetComprehensiveHealthAsync(cancellationToken);

        await Task.WhenAll(poolTask, cacheTask, resourceTask, uploadTask, dbTask, healthTask)
            .ConfigureAwait(false);

        return new OperateMetricsSnapshot(
            OperateMetricsMapper.MapConnectionPool(await poolTask.ConfigureAwait(false)),
            OperateMetricsMapper.MapCache(await cacheTask.ConfigureAwait(false)),
            OperateMetricsMapper.MapResources(await resourceTask.ConfigureAwait(false)),
            OperateMetricsMapper.MapUploadQueue(await uploadTask.ConfigureAwait(false)),
            OperateMetricsMapper.MapDatabaseResilience(await dbTask.ConfigureAwait(false)),
            OperateMetricsMapper.MapHealth(await healthTask.ConfigureAwait(false)));
    }
}

/// <summary>
/// Maps the bare monitoring wire responses into the Operate view models, translating
/// server health/utilization/pressure signals into the shared
/// <see cref="OperateStatus"/> color vocabulary (success/info/warning/danger/neutral).
/// </summary>
internal static class OperateMetricsMapper
{
    public static OperateMetricSection<OperateConnectionPoolMetric> MapConnectionPool(
        OperateSectionResult<ConnectionPoolMetricsResponse> result)
    {
        if (!result.IsAllowed)
        {
            return OperateMetricSection<OperateConnectionPoolMetric>.Denied(result.Status, result.Message);
        }

        var r = result.Value!;
        var utilization = RatioBar(r.Utilization, r.UtilizationPercentage, r.HasUtilizationData);
        var health = HealthStatusToOperate(r.HealthStatus, r.IsHealthy);

        OperateQueryAdmissionMetric? admission = null;
        if (r.QueryAdmission is { } qa)
        {
            var slotBar = qa.CurrentLimit > 0
                ? Bar((double)qa.InFlight / qa.CurrentLimit * 100.0,
                    $"{qa.InFlight} / {qa.CurrentLimit}",
                    SaturationStatus((double)qa.InFlight / qa.CurrentLimit),
                    hasData: true)
                : Bar(0, "no slots", Neutral("Query admission reports no logical limit."), hasData: false);

            admission = new OperateQueryAdmissionMetric(
                qa.AdaptiveEnabled,
                qa.CurrentLimit,
                qa.MinLimit,
                qa.MaxLimit,
                qa.InFlight,
                qa.AvailableSlots,
                qa.QueuedWaiters,
                qa.TargetDurationMs,
                qa.DurationEwmaMs,
                qa.QueueWaitEwmaMs,
                qa.AdjustmentCount,
                string.IsNullOrWhiteSpace(qa.LastAdjustmentDirection) ? "none" : qa.LastAdjustmentDirection!,
                slotBar);
        }

        return OperateMetricSection<OperateConnectionPoolMetric>.Allowed(new OperateConnectionPoolMetric(
            utilization,
            health,
            string.IsNullOrWhiteSpace(r.HealthStatus) ? health.Label : r.HealthStatus!,
            r.TotalFailures,
            r.TotalTimeouts,
            admission,
            FormatTimestamp(r.Timestamp)));
    }

    public static OperateMetricSection<OperateCacheMetric> MapCache(
        OperateSectionResult<CacheMetricsResponse> result)
    {
        if (!result.IsAllowed)
        {
            return OperateMetricSection<OperateCacheMetric>.Denied(result.Status, result.Message);
        }

        var r = result.Value!;
        // Hit ratio is "higher is better": green when healthy, danger when starved.
        var ratioStatus = r.IsHealthy
            ? Success($"Cache hit ratio is healthy ({Percent(r.HitRatio)}).")
            : r.HitRatio < 0.5
                ? Danger($"Cache hit ratio is critically low ({Percent(r.HitRatio)}).")
                : Warning($"Cache hit ratio is below the healthy threshold ({Percent(r.HitRatio)}).");

        var bar = Bar(r.HitRatio * 100.0, r.HitRatioPercentage ?? Percent(r.HitRatio), ratioStatus, hasData: true);

        return OperateMetricSection<OperateCacheMetric>.Allowed(new OperateCacheMetric(
            bar,
            ratioStatus,
            r.Recommendations ?? [],
            FormatTimestamp(r.Timestamp)));
    }

    public static OperateMetricSection<OperateResourceMetric> MapResources(
        OperateSectionResult<ResourceMetricsResponse> result)
    {
        if (!result.IsAllowed)
        {
            return OperateMetricSection<OperateResourceMetric>.Denied(result.Status, result.Message);
        }

        var r = result.Value!;
        var pressureStatus = PressureLevelToOperate(r.MemoryPressureLevel);
        var health = r.IsHealthy
            ? Success("Memory is within healthy bounds.")
            : Danger("Memory pressure is critical.");

        return OperateMetricSection<OperateResourceMetric>.Allowed(new OperateResourceMetric(
            r.MemoryUsageMB,
            r.MemoryUsageBytes,
            string.IsNullOrWhiteSpace(r.MemoryPressureLevel) ? "unknown" : r.MemoryPressureLevel!,
            pressureStatus,
            health,
            r.GcInfo?.Gen0Collections ?? 0,
            r.GcInfo?.Gen1Collections ?? 0,
            r.GcInfo?.Gen2Collections ?? 0,
            r.GcInfo?.TotalMemory ?? 0,
            FormatTimestamp(r.Timestamp)));
    }

    public static OperateMetricSection<OperateUploadQueueMetric> MapUploadQueue(
        OperateSectionResult<UploadQueueMetricsResponse> result)
    {
        if (!result.IsAllowed)
        {
            return OperateMetricSection<OperateUploadQueueMetric>.Denied(result.Status, result.Message);
        }

        var r = result.Value!;
        var depthStatus = r.IsHealthy
            ? Success($"Upload queue depth is healthy ({r.QueueDepth} of {r.MaxQueueDepth}).")
            : Warning($"Upload queue depth is elevated ({r.QueueDepth} of {r.MaxQueueDepth}).");

        var bar = Bar(
            r.QueueUtilization * 100.0,
            $"{r.QueueDepth} / {r.MaxQueueDepth}",
            depthStatus,
            hasData: r.MaxQueueDepth > 0);

        return OperateMetricSection<OperateUploadQueueMetric>.Allowed(new OperateUploadQueueMetric(
            bar,
            r.QueueDepth,
            r.MaxQueueDepth,
            r.ActiveUploads,
            r.MaxConcurrentUploads,
            depthStatus,
            FormatTimestamp(r.Timestamp)));
    }

    public static OperateMetricSection<OperateDatabaseResilienceMetric> MapDatabaseResilience(
        OperateSectionResult<DatabaseResilienceMetricsResponse> result)
    {
        if (!result.IsAllowed)
        {
            return OperateMetricSection<OperateDatabaseResilienceMetric>.Denied(result.Status, result.Message);
        }

        var r = result.Value!;
        var resilience = r.Resilience;
        var pool = r.ConnectionPool;

        var errorRate = resilience?.ErrorRate ?? 0.0;
        var errorStatus = errorRate > 0.1
            ? Danger($"Database query error rate is very high ({resilience?.ErrorRatePercentage ?? Percent(errorRate)}).")
            : errorRate > 0.05
                ? Warning($"Database query error rate is elevated ({resilience?.ErrorRatePercentage ?? Percent(errorRate)}).")
                : Success($"Database query error rate is healthy ({resilience?.ErrorRatePercentage ?? Percent(errorRate)}).");

        var errorBar = Bar(errorRate * 100.0, resilience?.ErrorRatePercentage ?? Percent(errorRate), errorStatus, hasData: true);

        var poolUtilization = RatioBar(
            pool?.Utilization ?? 0.0,
            pool?.UtilizationPercentage,
            pool?.HasUtilizationData ?? false);
        var poolHealth = HealthStatusToOperate(pool?.HealthStatus, pool?.IsHealthy ?? false);

        return OperateMetricSection<OperateDatabaseResilienceMetric>.Allowed(new OperateDatabaseResilienceMetric(
            errorBar,
            resilience?.CircuitBreakerEnabled ?? false,
            resilience?.ConnectionFailures ?? 0,
            resilience?.QueryFailures ?? 0,
            poolUtilization,
            poolHealth,
            string.IsNullOrWhiteSpace(pool?.HealthStatus) ? poolHealth.Label : pool!.HealthStatus!,
            r.Alerts ?? [],
            FormatTimestamp(r.Timestamp)));
    }

    public static OperateMetricSection<OperateComprehensiveHealthMetric> MapHealth(
        OperateSectionResult<ComprehensiveHealthResponse> result)
    {
        if (!result.IsAllowed)
        {
            return OperateMetricSection<OperateComprehensiveHealthMetric>.Denied(result.Status, result.Message);
        }

        var r = result.Value!;
        var status = HealthReportStatusToOperate(r.Status);
        var entries = (r.Entries ?? [])
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair =>
            {
                var entryStatus = HealthReportStatusToOperate(pair.Value.Status);
                return new OperateHealthCheckEntry(
                    pair.Key,
                    entryStatus,
                    string.IsNullOrWhiteSpace(pair.Value.Status) ? entryStatus.Label : pair.Value.Status!,
                    FormatDuration(pair.Value.Duration),
                    string.IsNullOrWhiteSpace(pair.Value.Description) ? string.Empty : pair.Value.Description!);
            })
            .ToArray();

        return OperateMetricSection<OperateComprehensiveHealthMetric>.Allowed(new OperateComprehensiveHealthMetric(
            status,
            string.IsNullOrWhiteSpace(r.Status) ? status.Label : r.Status!,
            FormatDuration(r.TotalDuration),
            entries,
            FormatTimestamp(r.Timestamp)));
    }

    // --- status mapping helpers -------------------------------------------------

    private static OperateStatus HealthStatusToOperate(string? healthStatus, bool isHealthy)
    {
        if (string.IsNullOrWhiteSpace(healthStatus))
        {
            return isHealthy ? Success("Healthy.") : Neutral("Health status was not reported.");
        }

        return healthStatus.Trim().ToLowerInvariant() switch
        {
            "healthy" => Success("Connection pool is healthy."),
            "degraded" => Warning("Connection pool is degraded."),
            "unknown" => Neutral("Connection pool health is unknown."),
            _ => isHealthy ? Success(healthStatus) : Warning(healthStatus)
        };
    }

    private static OperateStatus HealthReportStatusToOperate(string? status) =>
        (status?.Trim().ToLowerInvariant()) switch
        {
            "healthy" => Success("Healthy."),
            "degraded" => Warning("Degraded."),
            "unhealthy" => Danger("Unhealthy."),
            null or "" => Neutral("Status not reported."),
            _ => Neutral(status!)
        };

    private static OperateStatus PressureLevelToOperate(string? level) =>
        (level?.Trim().ToLowerInvariant()) switch
        {
            "low" or "normal" => Success($"Memory pressure is {level}."),
            "medium" or "moderate" or "elevated" => Warning($"Memory pressure is {level}."),
            "high" or "critical" => Danger($"Memory pressure is {level}."),
            null or "" => Neutral("Memory pressure level was not reported."),
            _ => Neutral($"Memory pressure is {level}.")
        };

    // Utilization/saturation are "lower is better": green up to 80%, warning to 90%, danger above.
    private static OperateStatus SaturationStatus(double ratio) => ratio switch
    {
        > 0.9 => Danger($"Saturation is very high ({Percent(ratio)})."),
        > 0.8 => Warning($"Saturation is high ({Percent(ratio)})."),
        _ => Success($"Saturation is healthy ({Percent(ratio)}).")
    };

    private static OperateMetricBar RatioBar(double ratio, string? displayPercentage, bool hasData)
    {
        if (!hasData)
        {
            return Bar(0, "unavailable", Neutral("Utilization telemetry is unavailable for the active provider."), hasData: false);
        }

        return Bar(ratio * 100.0, displayPercentage ?? Percent(ratio), SaturationStatus(ratio), hasData: true);
    }

    private static OperateMetricBar Bar(double percent, string displayValue, OperateStatus status, bool hasData) =>
        new(percent, displayValue, status, hasData);

    private static OperateStatus Success(string description) => new("healthy", description);

    private static OperateStatus Warning(string description) => new("warning", description);

    private static OperateStatus Danger(string description) => new("critical", description);

    private static OperateStatus Neutral(string description) => new("unknown", description);

    private static string Percent(double ratio) =>
        (ratio * 100.0).ToString("0.0", CultureInfo.InvariantCulture) + "%";

    private static string FormatTimestamp(DateTimeOffset timestamp) =>
        timestamp == default
            ? "unknown"
            : timestamp.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);

    private static string FormatDuration(TimeSpan duration) =>
        duration.TotalMilliseconds < 1000
            ? $"{duration.TotalMilliseconds.ToString("0.0", CultureInfo.InvariantCulture)} ms"
            : $"{duration.TotalSeconds.ToString("0.00", CultureInfo.InvariantCulture)} s";
}
