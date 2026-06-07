using System.Text.Json.Serialization;

namespace Honua.Console.Contracts;

// SHIM(honua-sdk-dotnet): The production-monitoring metrics/health contracts exposed
// by honua-server (Features/Infrastructure/Monitoring/ProductionMonitoringEndpoints.cs,
// group "/monitoring", admin-authorized via X-API-Key) are not yet projected to
// honua-sdk-dotnet. These records mirror the server HTTP wire surface (NOT the server's
// internal monitoring domain models) and are consumed through the single Console shim
// boundary, exactly like OperateObservabilityContracts and MetadataReleaseContracts.
//
// IMPORTANT: every monitoring handler returns a BARE Results.Json payload (NO
// ApiResponse envelope). JSON on the wire is camelCase.
//
// Route map (concrete), all under /monitoring, admin-authorized (X-API-Key):
//   GET /monitoring/metrics/connection-pool    -> ConnectionPoolMetricsResponse
//   GET /monitoring/metrics/cache              -> CacheMetricsResponse
//   GET /monitoring/metrics/resources          -> ResourceMetricsResponse
//   GET /monitoring/metrics/upload-queue       -> UploadQueueMetricsResponse
//   GET /monitoring/metrics/database-resilience-> DatabaseResilienceMetricsResponse
//   GET /monitoring/health/comprehensive       -> ComprehensiveHealthResponse

/// <summary>
/// Concrete monitoring routes, kept in one place so the client and tests share the
/// exact server paths. Relative (no leading slash); the client's BuildUri normalizes.
/// </summary>
public static class MonitoringMetricsRoutes
{
    public const string ConnectionPool = "monitoring/metrics/connection-pool";
    public const string Cache = "monitoring/metrics/cache";
    public const string Resources = "monitoring/metrics/resources";
    public const string UploadQueue = "monitoring/metrics/upload-queue";
    public const string DatabaseResilience = "monitoring/metrics/database-resilience";
    public const string ComprehensiveHealth = "monitoring/health/comprehensive";
}

/// <summary>Wire model for <c>GET /monitoring/metrics/connection-pool</c>.</summary>
public sealed class ConnectionPoolMetricsResponse
{
    [JsonPropertyName("utilization")]
    public double Utilization { get; set; }

    [JsonPropertyName("hasUtilizationData")]
    public bool HasUtilizationData { get; set; }

    [JsonPropertyName("utilizationStatus")]
    public string? UtilizationStatus { get; set; }

    [JsonPropertyName("utilizationPercentage")]
    public string? UtilizationPercentage { get; set; }

    [JsonPropertyName("totalFailures")]
    public long TotalFailures { get; set; }

    [JsonPropertyName("totalTimeouts")]
    public long TotalTimeouts { get; set; }

    [JsonPropertyName("healthStatus")]
    public string? HealthStatus { get; set; }

    [JsonPropertyName("isHealthy")]
    public bool IsHealthy { get; set; }

    [JsonPropertyName("queryAdmission")]
    public QueryAdmissionMetricsResponse? QueryAdmission { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; set; }
}

/// <summary>Provider-level query admission-control telemetry (nullable when the active provider omits it).</summary>
public sealed class QueryAdmissionMetricsResponse
{
    [JsonPropertyName("adaptiveEnabled")]
    public bool AdaptiveEnabled { get; set; }

    [JsonPropertyName("currentLimit")]
    public int CurrentLimit { get; set; }

    [JsonPropertyName("minLimit")]
    public int MinLimit { get; set; }

    [JsonPropertyName("maxLimit")]
    public int MaxLimit { get; set; }

    [JsonPropertyName("inFlight")]
    public int InFlight { get; set; }

    [JsonPropertyName("availableSlots")]
    public int AvailableSlots { get; set; }

    [JsonPropertyName("queuedWaiters")]
    public int QueuedWaiters { get; set; }

    [JsonPropertyName("targetDurationMs")]
    public double TargetDurationMs { get; set; }

    [JsonPropertyName("durationEwmaMs")]
    public double DurationEwmaMs { get; set; }

    [JsonPropertyName("queueWaitEwmaMs")]
    public double QueueWaitEwmaMs { get; set; }

    [JsonPropertyName("adjustmentCount")]
    public long AdjustmentCount { get; set; }

    [JsonPropertyName("lastAdjustmentDirection")]
    public string? LastAdjustmentDirection { get; set; }
}

/// <summary>Wire model for <c>GET /monitoring/metrics/cache</c>.</summary>
public sealed class CacheMetricsResponse
{
    [JsonPropertyName("hitRatio")]
    public double HitRatio { get; set; }

    [JsonPropertyName("hitRatioPercentage")]
    public string? HitRatioPercentage { get; set; }

    [JsonPropertyName("isHealthy")]
    public bool IsHealthy { get; set; }

    [JsonPropertyName("recommendations")]
    public List<string>? Recommendations { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; set; }
}

/// <summary>Wire model for <c>GET /monitoring/metrics/resources</c>.</summary>
public sealed class ResourceMetricsResponse
{
    [JsonPropertyName("memoryUsageBytes")]
    public long MemoryUsageBytes { get; set; }

    [JsonPropertyName("memoryUsageMB")]
    public long MemoryUsageMB { get; set; }

    [JsonPropertyName("memoryPressureLevel")]
    public string? MemoryPressureLevel { get; set; }

    [JsonPropertyName("gcInfo")]
    public ResourceGcInfoResponse? GcInfo { get; set; }

    [JsonPropertyName("isHealthy")]
    public bool IsHealthy { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; set; }
}

/// <summary>Garbage-collection counters nested under the resource metrics.</summary>
public sealed class ResourceGcInfoResponse
{
    [JsonPropertyName("gen0Collections")]
    public int Gen0Collections { get; set; }

    [JsonPropertyName("gen1Collections")]
    public int Gen1Collections { get; set; }

    [JsonPropertyName("gen2Collections")]
    public int Gen2Collections { get; set; }

    [JsonPropertyName("totalMemory")]
    public long TotalMemory { get; set; }

    [JsonPropertyName("lastGen0Time")]
    public DateTimeOffset LastGen0Time { get; set; }
}

/// <summary>Wire model for <c>GET /monitoring/metrics/upload-queue</c>.</summary>
public sealed class UploadQueueMetricsResponse
{
    [JsonPropertyName("queueDepth")]
    public int QueueDepth { get; set; }

    [JsonPropertyName("maxQueueDepth")]
    public int MaxQueueDepth { get; set; }

    [JsonPropertyName("activeUploads")]
    public int ActiveUploads { get; set; }

    [JsonPropertyName("maxConcurrentUploads")]
    public int MaxConcurrentUploads { get; set; }

    [JsonPropertyName("queueUtilization")]
    public double QueueUtilization { get; set; }

    [JsonPropertyName("isHealthy")]
    public bool IsHealthy { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; set; }
}

/// <summary>Wire model for <c>GET /monitoring/metrics/database-resilience</c>.</summary>
public sealed class DatabaseResilienceMetricsResponse
{
    [JsonPropertyName("connectionPool")]
    public DatabaseConnectionPoolMetricsResponse? ConnectionPool { get; set; }

    [JsonPropertyName("resilience")]
    public DatabaseResilienceStatusResponse? Resilience { get; set; }

    [JsonPropertyName("alerts")]
    public List<string>? Alerts { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; set; }
}

/// <summary>Connection-pool block nested in the database-resilience response.</summary>
public sealed class DatabaseConnectionPoolMetricsResponse
{
    [JsonPropertyName("utilization")]
    public double Utilization { get; set; }

    [JsonPropertyName("hasUtilizationData")]
    public bool HasUtilizationData { get; set; }

    [JsonPropertyName("utilizationStatus")]
    public string? UtilizationStatus { get; set; }

    [JsonPropertyName("utilizationPercentage")]
    public string? UtilizationPercentage { get; set; }

    [JsonPropertyName("activeConnections")]
    public int ActiveConnections { get; set; }

    [JsonPropertyName("totalFailures")]
    public long TotalFailures { get; set; }

    [JsonPropertyName("totalTimeouts")]
    public long TotalTimeouts { get; set; }

    [JsonPropertyName("healthStatus")]
    public string? HealthStatus { get; set; }

    [JsonPropertyName("isHealthy")]
    public bool IsHealthy { get; set; }
}

/// <summary>Resilience/circuit-breaker block nested in the database-resilience response.</summary>
public sealed class DatabaseResilienceStatusResponse
{
    [JsonPropertyName("circuitBreakerEnabled")]
    public bool CircuitBreakerEnabled { get; set; }

    [JsonPropertyName("connectionFailures")]
    public long ConnectionFailures { get; set; }

    [JsonPropertyName("queryFailures")]
    public long QueryFailures { get; set; }

    [JsonPropertyName("errorRate")]
    public double ErrorRate { get; set; }

    [JsonPropertyName("errorRatePercentage")]
    public string? ErrorRatePercentage { get; set; }
}

/// <summary>Wire model for <c>GET /monitoring/health/comprehensive</c>.</summary>
public sealed class ComprehensiveHealthResponse
{
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("totalDuration")]
    public TimeSpan TotalDuration { get; set; }

    [JsonPropertyName("entries")]
    public Dictionary<string, HealthCheckEntryResponse>? Entries { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; set; }
}

/// <summary>Individual health-check entry in the comprehensive health report.</summary>
public sealed class HealthCheckEntryResponse
{
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("duration")]
    public TimeSpan Duration { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

/// <summary>
/// Source-generated JSON context for the monitoring metrics/health wire contracts
/// (trim/AOT safe), mirroring MetadataReleaseJsonContext.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(ConnectionPoolMetricsResponse))]
[JsonSerializable(typeof(CacheMetricsResponse))]
[JsonSerializable(typeof(ResourceMetricsResponse))]
[JsonSerializable(typeof(UploadQueueMetricsResponse))]
[JsonSerializable(typeof(DatabaseResilienceMetricsResponse))]
[JsonSerializable(typeof(ComprehensiveHealthResponse))]
public sealed partial class MonitoringMetricsJsonContext : JsonSerializerContext
{
}
