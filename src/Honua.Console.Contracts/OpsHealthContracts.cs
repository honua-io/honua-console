using System.Text.Json.Serialization;

namespace Honua.Console.Contracts;

// The handler returns a bare JSON payload, not an ApiResponse envelope.

// Relative paths have no leading slash; BuildUri normalizes them.
public static class OpsHealthRoutes
{
    [OpsParityRoute("GET")]
    public const string Snapshot = "api/v1/admin/observability/ops-health";

    // Accepts window, resolution, and perReplica. There is no Last-Event-ID; a dropped connection backfills by requesting this window again.
    [OpsParityRoute("GET")]
    public const string History = "api/v1/admin/observability/ops-health/history";
}

public sealed class OpsHealthSnapshotResponse
{
    [JsonPropertyName("generatedAt")]
    public DateTimeOffset GeneratedAt { get; set; }

    [JsonPropertyName("managedTargets")]
    public List<OpsManagedTargetResponse>? ManagedTargets { get; set; }

    // One of Healthy, Degraded, or Unhealthy.
    [JsonPropertyName("overallStatus")]
    public string? OverallStatus { get; set; }

    [JsonPropertyName("health")]
    public OpsHealthChecksResponse? Health { get; set; }

    [JsonPropertyName("servingLatency")]
    public OpsServingLatencyResponse? ServingLatency { get; set; }

    [JsonPropertyName("geoprocessing")]
    public OpsGpQueueResponse? Geoprocessing { get; set; }

    [JsonPropertyName("alertDispatch")]
    public OpsAlertDispatchResponse? AlertDispatch { get; set; }

    [JsonPropertyName("deploy")]
    public OpsDeployReadinessResponse? Deploy { get; set; }

    [JsonPropertyName("database")]
    public OpsDatabaseResponse? Database { get; set; }
}

public sealed class OpsManagedTargetResponse
{
    [JsonPropertyName("targetId")]
    public string? TargetId { get; set; }
    [JsonPropertyName("available")]
    public bool Available { get; set; }
    [JsonPropertyName("services")]
    public List<OpsManagedServiceResponse>? Services { get; set; }
}

public sealed class OpsManagedServiceResponse
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }
    [JsonPropertyName("status")]
    public string? Status { get; set; }
}

public sealed class OpsHealthChecksResponse
{
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("totalDurationMs")]
    public double TotalDurationMs { get; set; }

    [JsonPropertyName("entries")]
    public List<OpsHealthCheckEntryResponse>? Entries { get; set; }
}

public sealed class OpsHealthCheckEntryResponse
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("durationMs")]
    public double DurationMs { get; set; }
}

public sealed class OpsServingLatencyResponse
{
    [JsonPropertyName("windowSeconds")]
    public double WindowSeconds { get; set; }

    [JsonPropertyName("protocols")]
    public List<OpsServingLatencyProtocolResponse>? Protocols { get; set; }
}

public sealed class OpsServingLatencyProtocolResponse
{
    [JsonPropertyName("protocol")]
    public string? Protocol { get; set; }

    [JsonPropertyName("requestCount")]
    public long RequestCount { get; set; }

    // Counts responses at HTTP status 500 or above.
    [JsonPropertyName("errorCount")]
    public long ErrorCount { get; set; }

    [JsonPropertyName("errorRate")]
    public double ErrorRate { get; set; }

    [JsonPropertyName("p50Ms")]
    public double P50Ms { get; set; }

    [JsonPropertyName("p95Ms")]
    public double P95Ms { get; set; }

    [JsonPropertyName("p99Ms")]
    public double P99Ms { get; set; }

    [JsonPropertyName("maxMs")]
    public double MaxMs { get; set; }
}

public sealed class OpsGpQueueResponse
{
    [JsonPropertyName("totalActive")]
    public int TotalActive { get; set; }

    [JsonPropertyName("available")]
    public bool Available { get; set; }

    [JsonPropertyName("buckets")]
    public List<OpsGpQueueBucketResponse>? Buckets { get; set; }
}

public sealed class OpsGpQueueBucketResponse
{
    // A non-terminal status: Queued, Provisioning, or Running.
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("backend")]
    public string? Backend { get; set; }

    [JsonPropertyName("count")]
    public int Count { get; set; }
}

public sealed class OpsAlertDispatchResponse
{
    [JsonPropertyName("dispatcherRunning")]
    public bool DispatcherRunning { get; set; }

    [JsonPropertyName("dispatcherEnabled")]
    public bool DispatcherEnabled { get; set; }

    // The most recent dispatch pass could not reach the backlog store.
    [JsonPropertyName("storagePollFailing")]
    public bool StoragePollFailing { get; set; }

    [JsonPropertyName("lastPollAt")]
    public DateTimeOffset? LastPollAt { get; set; }

    [JsonPropertyName("pendingCount")]
    public long? PendingCount { get; set; }

    [JsonPropertyName("deadLetteredCount")]
    public long? DeadLetteredCount { get; set; }
}

public sealed class OpsDeployReadinessResponse
{
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("readyForCoordinatedDeploy")]
    public bool ReadyForCoordinatedDeploy { get; set; }

    [JsonPropertyName("pendingMigrationsCount")]
    public int PendingMigrationsCount { get; set; }

    [JsonPropertyName("pendingContractScriptsCount")]
    public int PendingContractScriptsCount { get; set; }

    [JsonPropertyName("platformRelease")]
    public OpsPlatformReleaseResponse? PlatformRelease { get; set; }
}

public sealed class OpsPlatformReleaseResponse
{
    [JsonPropertyName("releaseVersion")]
    public string? ReleaseVersion { get; set; }

    [JsonPropertyName("releaseDeclared")]
    public bool ReleaseDeclared { get; set; }

    [JsonPropertyName("isCoVersioned")]
    public bool IsCoVersioned { get; set; }

    [JsonPropertyName("skewedIds")]
    public List<string>? SkewedIds { get; set; }
}

public sealed class OpsDatabaseResponse
{
    [JsonPropertyName("connectionPoolUtilization")]
    public double? ConnectionPoolUtilization { get; set; }

    [JsonPropertyName("hasConnectionPoolData")]
    public bool HasConnectionPoolData { get; set; }

    [JsonPropertyName("activeConnections")]
    public int ActiveConnections { get; set; }

    [JsonPropertyName("connectionAcquisitionTimeouts")]
    public long ConnectionAcquisitionTimeouts { get; set; }

    [JsonPropertyName("connectionAcquisitionFailures")]
    public long ConnectionAcquisitionFailures { get; set; }

    [JsonPropertyName("cacheHitRatio")]
    public double CacheHitRatio { get; set; }

    [JsonPropertyName("errorRate")]
    public double ErrorRate { get; set; }
}

public sealed class OpsHealthHistoryResponse
{
    [JsonPropertyName("generatedAt")]
    public DateTimeOffset GeneratedAt { get; set; }

    [JsonPropertyName("resolution")]
    public string? Resolution { get; set; }

    // The server clamps this window to the tier's retention cap.
    [JsonPropertyName("windowSeconds")]
    public double WindowSeconds { get; set; }

    [JsonPropertyName("from")]
    public DateTimeOffset From { get; set; }

    [JsonPropertyName("to")]
    public DateTimeOffset To { get; set; }

    [JsonPropertyName("perReplica")]
    public bool PerReplica { get; set; }

    [JsonPropertyName("latency")]
    public List<OpsHealthHistoryLatencySeriesResponse>? Latency { get; set; }

    [JsonPropertyName("vitals")]
    public List<OpsHealthHistoryVitalsPointResponse>? Vitals { get; set; }
}

public sealed class OpsHealthHistoryLatencySeriesResponse
{
    [JsonPropertyName("protocol")]
    public string? Protocol { get; set; }

    // Null means the series is cluster-merged rather than one replica.
    [JsonPropertyName("replicaId")]
    public string? ReplicaId { get; set; }

    [JsonPropertyName("points")]
    public List<OpsHealthHistoryLatencyPointResponse>? Points { get; set; }
}

public sealed class OpsHealthHistoryLatencyPointResponse
{
    [JsonPropertyName("bucketStart")]
    public DateTimeOffset BucketStart { get; set; }

    [JsonPropertyName("requestCount")]
    public long RequestCount { get; set; }

    // Counts responses at HTTP status 500 or above.
    [JsonPropertyName("errorCount")]
    public long ErrorCount { get; set; }

    [JsonPropertyName("errorRate")]
    public double ErrorRate { get; set; }

    [JsonPropertyName("p50Ms")]
    public double P50Ms { get; set; }

    [JsonPropertyName("p95Ms")]
    public double P95Ms { get; set; }

    [JsonPropertyName("p99Ms")]
    public double P99Ms { get; set; }

    [JsonPropertyName("maxMs")]
    public double MaxMs { get; set; }
}

public sealed class OpsHealthHistoryVitalsPointResponse
{
    [JsonPropertyName("bucketStart")]
    public DateTimeOffset BucketStart { get; set; }

    // Null means the series is cluster-merged rather than one replica.
    [JsonPropertyName("replicaId")]
    public string? ReplicaId { get; set; }

    // One of Healthy, Degraded, or Unhealthy.
    [JsonPropertyName("overallStatus")]
    public string? OverallStatus { get; set; }

    [JsonPropertyName("gpQueueTotal")]
    public int GpQueueTotal { get; set; }

    // Keys are "<status>|<backend>".
    [JsonPropertyName("gpQueueBreakdown")]
    public Dictionary<string, int>? GpQueueBreakdown { get; set; }

    [JsonPropertyName("alertPending")]
    public long? AlertPending { get; set; }

    [JsonPropertyName("alertDeadLettered")]
    public long? AlertDeadLettered { get; set; }

    [JsonPropertyName("dbPoolUtilization")]
    public double? DbPoolUtilization { get; set; }

    [JsonPropertyName("dbActiveConnections")]
    public int DbActiveConnections { get; set; }

    [JsonPropertyName("cacheHitRatio")]
    public double CacheHitRatio { get; set; }

    [JsonPropertyName("errorRate")]
    public double ErrorRate { get; set; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(OpsHealthSnapshotResponse))]
[JsonSerializable(typeof(OpsHealthHistoryResponse))]
public sealed partial class OpsHealthJsonContext : JsonSerializerContext
{
}
