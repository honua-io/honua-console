using System.Text.Json.Serialization;

namespace Honua.Console.Contracts;

// SHIM(honua-sdk-dotnet): The honua-server consolidated ops-health snapshot endpoint
// (Features/Admin/OpsObservabilityEndpoints.cs, group
// "/api/v{version}/admin/observability", admin-authorized via X-API-Key) is not yet
// projected to honua-sdk-dotnet. These records mirror the server HTTP wire surface
// (OpsObservabilityModels.cs) — NOT the server's internal monitoring domain models —
// and are consumed through the single Console shim boundary, exactly like
// MonitoringMetricsContracts and OperateObservabilityContracts.
//
// IMPORTANT: the handler returns a BARE Results.Json payload (NO ApiResponse
// envelope). JSON on the wire is camelCase; null-valued properties are omitted.
//
// Route (concrete v1), admin-authorized (X-API-Key):
//   GET /api/v1/admin/observability/ops-health -> OpsHealthSnapshotResponse

/// <summary>
/// Concrete v1 route for the honua-server ops-health snapshot endpoint, kept in one
/// place so the client and tests share the exact server path. Relative (no leading
/// slash); the client's BuildUri normalizes.
/// </summary>
public static class OpsHealthRoutes
{
    /// <summary>The consolidated ops-health snapshot route.</summary>
    public const string Snapshot = "api/v1/admin/observability/ops-health";
}

/// <summary>
/// Consolidated operational-health snapshot returned by
/// <c>GET /api/v1/admin/observability/ops-health</c>. Mirrors the server
/// <c>OpsHealthSnapshotResponse</c>.
/// </summary>
public sealed class OpsHealthSnapshotResponse
{
    /// <summary>Gets or sets the UTC time the snapshot was produced.</summary>
    [JsonPropertyName("generatedAt")]
    public DateTimeOffset GeneratedAt { get; set; }

    /// <summary>Gets or sets the overall status roll-up (Healthy/Degraded/Unhealthy).</summary>
    [JsonPropertyName("overallStatus")]
    public string? OverallStatus { get; set; }

    /// <summary>Gets or sets the comprehensive health-check roll-up.</summary>
    [JsonPropertyName("health")]
    public OpsHealthChecksResponse? Health { get; set; }

    /// <summary>Gets or sets the in-process serving-latency snapshot.</summary>
    [JsonPropertyName("servingLatency")]
    public OpsServingLatencyResponse? ServingLatency { get; set; }

    /// <summary>Gets or sets the geoprocessing queue-depth summary.</summary>
    [JsonPropertyName("geoprocessing")]
    public OpsGpQueueResponse? Geoprocessing { get; set; }

    /// <summary>Gets or sets the alert-dispatch backlog / dead-letter summary.</summary>
    [JsonPropertyName("alertDispatch")]
    public OpsAlertDispatchResponse? AlertDispatch { get; set; }

    /// <summary>Gets or sets the coordinated-deploy readiness summary and platform-release skew.</summary>
    [JsonPropertyName("deploy")]
    public OpsDeployReadinessResponse? Deploy { get; set; }

    /// <summary>Gets or sets the database connection-pool utilization and cache/error metrics.</summary>
    [JsonPropertyName("database")]
    public OpsDatabaseResponse? Database { get; set; }
}

/// <summary>Comprehensive health-check roll-up wire model.</summary>
public sealed class OpsHealthChecksResponse
{
    /// <summary>Gets or sets the aggregate health status string.</summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    /// <summary>Gets or sets the total health-check evaluation duration in milliseconds.</summary>
    [JsonPropertyName("totalDurationMs")]
    public double TotalDurationMs { get; set; }

    /// <summary>Gets or sets the individual health-check entries.</summary>
    [JsonPropertyName("entries")]
    public List<OpsHealthCheckEntryResponse>? Entries { get; set; }
}

/// <summary>A single health-check entry wire model.</summary>
public sealed class OpsHealthCheckEntryResponse
{
    /// <summary>Gets or sets the registered health-check name.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Gets or sets the check status string.</summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    /// <summary>Gets or sets the operator-facing description, when provided.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>Gets or sets the check duration in milliseconds.</summary>
    [JsonPropertyName("durationMs")]
    public double DurationMs { get; set; }
}

/// <summary>In-process serving-latency snapshot wire model.</summary>
public sealed class OpsServingLatencyResponse
{
    /// <summary>Gets or sets the rolling window length in seconds.</summary>
    [JsonPropertyName("windowSeconds")]
    public double WindowSeconds { get; set; }

    /// <summary>Gets or sets the per-protocol latency entries.</summary>
    [JsonPropertyName("protocols")]
    public List<OpsServingLatencyProtocolResponse>? Protocols { get; set; }
}

/// <summary>Per-protocol serving-latency entry wire model.</summary>
public sealed class OpsServingLatencyProtocolResponse
{
    /// <summary>Gets or sets the protocol family.</summary>
    [JsonPropertyName("protocol")]
    public string? Protocol { get; set; }

    /// <summary>Gets or sets the in-window request count.</summary>
    [JsonPropertyName("requestCount")]
    public long RequestCount { get; set; }

    /// <summary>Gets or sets the in-window server-error count (HTTP status &gt;= 500).</summary>
    [JsonPropertyName("errorCount")]
    public long ErrorCount { get; set; }

    /// <summary>Gets or sets the server-error rate over the window (0.0 to 1.0).</summary>
    [JsonPropertyName("errorRate")]
    public double ErrorRate { get; set; }

    /// <summary>Gets or sets the 50th-percentile duration in milliseconds.</summary>
    [JsonPropertyName("p50Ms")]
    public double P50Ms { get; set; }

    /// <summary>Gets or sets the 95th-percentile duration in milliseconds.</summary>
    [JsonPropertyName("p95Ms")]
    public double P95Ms { get; set; }

    /// <summary>Gets or sets the 99th-percentile duration in milliseconds.</summary>
    [JsonPropertyName("p99Ms")]
    public double P99Ms { get; set; }

    /// <summary>Gets or sets the maximum duration in milliseconds.</summary>
    [JsonPropertyName("maxMs")]
    public double MaxMs { get; set; }
}

/// <summary>Geoprocessing queue-depth wire model.</summary>
public sealed class OpsGpQueueResponse
{
    /// <summary>Gets or sets the total active jobs (queued + provisioning + running).</summary>
    [JsonPropertyName("totalActive")]
    public int TotalActive { get; set; }

    /// <summary>Gets or sets a value indicating whether GP queue telemetry is available.</summary>
    [JsonPropertyName("available")]
    public bool Available { get; set; }

    /// <summary>Gets or sets the active-job counts bucketed by status and backend.</summary>
    [JsonPropertyName("buckets")]
    public List<OpsGpQueueBucketResponse>? Buckets { get; set; }
}

/// <summary>A single GP queue-depth bucket wire model.</summary>
public sealed class OpsGpQueueBucketResponse
{
    /// <summary>Gets or sets the non-terminal job status (Queued/Provisioning/Running).</summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    /// <summary>Gets or sets the batch-compute backend identifier.</summary>
    [JsonPropertyName("backend")]
    public string? Backend { get; set; }

    /// <summary>Gets or sets the active-job count in this status/backend bucket.</summary>
    [JsonPropertyName("count")]
    public int Count { get; set; }
}

/// <summary>Alert-dispatch backlog / dead-letter summary wire model.</summary>
public sealed class OpsAlertDispatchResponse
{
    /// <summary>Gets or sets a value indicating whether the dispatcher loop is running.</summary>
    [JsonPropertyName("dispatcherRunning")]
    public bool DispatcherRunning { get; set; }

    /// <summary>Gets or sets a value indicating whether the alert pipeline is enabled by configuration.</summary>
    [JsonPropertyName("dispatcherEnabled")]
    public bool DispatcherEnabled { get; set; }

    /// <summary>Gets or sets a value indicating whether the most recent dispatch pass failed to reach the backlog store.</summary>
    [JsonPropertyName("storagePollFailing")]
    public bool StoragePollFailing { get; set; }

    /// <summary>Gets or sets the timestamp of the most recent successful dispatch pass, when known.</summary>
    [JsonPropertyName("lastPollAt")]
    public DateTimeOffset? LastPollAt { get; set; }

    /// <summary>Gets or sets the pending backlog count, when a backlog snapshot is available.</summary>
    [JsonPropertyName("pendingCount")]
    public long? PendingCount { get; set; }

    /// <summary>Gets or sets the dead-lettered count, when a backlog snapshot is available.</summary>
    [JsonPropertyName("deadLetteredCount")]
    public long? DeadLetteredCount { get; set; }
}

/// <summary>Coordinated-deploy readiness and platform-release skew wire model.</summary>
public sealed class OpsDeployReadinessResponse
{
    /// <summary>Gets or sets the preflight status string (ready/blocked).</summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    /// <summary>Gets or sets a value indicating whether the instance is ready for a coordinated deploy.</summary>
    [JsonPropertyName("readyForCoordinatedDeploy")]
    public bool ReadyForCoordinatedDeploy { get; set; }

    /// <summary>Gets or sets the count of pending (non-contract) migration scripts.</summary>
    [JsonPropertyName("pendingMigrationsCount")]
    public int PendingMigrationsCount { get; set; }

    /// <summary>Gets or sets the count of pending contract migration scripts.</summary>
    [JsonPropertyName("pendingContractScriptsCount")]
    public int PendingContractScriptsCount { get; set; }

    /// <summary>Gets or sets the platform-release skew summary.</summary>
    [JsonPropertyName("platformRelease")]
    public OpsPlatformReleaseResponse? PlatformRelease { get; set; }
}

/// <summary>Platform-release co-versioning summary wire model.</summary>
public sealed class OpsPlatformReleaseResponse
{
    /// <summary>Gets or sets the declared release version, when any.</summary>
    [JsonPropertyName("releaseVersion")]
    public string? ReleaseVersion { get; set; }

    /// <summary>Gets or sets a value indicating whether a platform release is declared.</summary>
    [JsonPropertyName("releaseDeclared")]
    public bool ReleaseDeclared { get; set; }

    /// <summary>Gets or sets a value indicating whether all planes are co-versioned with the release.</summary>
    [JsonPropertyName("isCoVersioned")]
    public bool IsCoVersioned { get; set; }

    /// <summary>Gets or sets the identifiers of planes skewed from the declared release.</summary>
    [JsonPropertyName("skewedIds")]
    public List<string>? SkewedIds { get; set; }
}

/// <summary>Database connection-pool + cache/error utilization wire model.</summary>
public sealed class OpsDatabaseResponse
{
    /// <summary>Gets or sets the connection-pool utilization ratio (0.0 to 1.0), when available.</summary>
    [JsonPropertyName("connectionPoolUtilization")]
    public double? ConnectionPoolUtilization { get; set; }

    /// <summary>Gets or sets a value indicating whether connection-pool utilization telemetry is available.</summary>
    [JsonPropertyName("hasConnectionPoolData")]
    public bool HasConnectionPoolData { get; set; }

    /// <summary>Gets or sets the number of active database connections.</summary>
    [JsonPropertyName("activeConnections")]
    public int ActiveConnections { get; set; }

    /// <summary>Gets or sets the connection acquisition timeout count.</summary>
    [JsonPropertyName("connectionAcquisitionTimeouts")]
    public long ConnectionAcquisitionTimeouts { get; set; }

    /// <summary>Gets or sets the connection acquisition failure count.</summary>
    [JsonPropertyName("connectionAcquisitionFailures")]
    public long ConnectionAcquisitionFailures { get; set; }

    /// <summary>Gets or sets the cache hit ratio (0.0 to 1.0).</summary>
    [JsonPropertyName("cacheHitRatio")]
    public double CacheHitRatio { get; set; }

    /// <summary>Gets or sets the live HTTP server error rate (0.0 to 1.0).</summary>
    [JsonPropertyName("errorRate")]
    public double ErrorRate { get; set; }
}

/// <summary>
/// Source-generated JSON context for the ops-health snapshot wire contract (trim/AOT
/// safe), mirroring MonitoringMetricsJsonContext.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(OpsHealthSnapshotResponse))]
public sealed partial class OpsHealthJsonContext : JsonSerializerContext
{
}
