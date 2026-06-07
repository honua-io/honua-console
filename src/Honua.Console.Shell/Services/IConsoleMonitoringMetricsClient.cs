using Honua.Console.Contracts;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Reads honua-server's production-monitoring metrics/health surface (group
/// <c>/monitoring</c>, admin-authorized via <c>X-API-Key</c>, bare JSON — NO
/// ApiResponse envelope) from the active environment profile's server. Each read is
/// independently permissioned/optional, so every method returns an
/// <see cref="OperateSectionResult{T}"/> whose status drives the shared
/// missing/forbidden/unsupported/unavailable surfaces, mirroring
/// <see cref="IConsoleGitOpsReleaseClient"/>. Per the Console Patterns Charter
/// section 11 the client never returns seeded metrics; with no environment bound
/// every read returns a missing-binding result.
/// </summary>
public interface IConsoleMonitoringMetricsClient
{
    Task<OperateSectionResult<ConnectionPoolMetricsResponse>> GetConnectionPoolMetricsAsync(
        CancellationToken cancellationToken = default);

    Task<OperateSectionResult<CacheMetricsResponse>> GetCacheMetricsAsync(
        CancellationToken cancellationToken = default);

    Task<OperateSectionResult<ResourceMetricsResponse>> GetResourceMetricsAsync(
        CancellationToken cancellationToken = default);

    Task<OperateSectionResult<UploadQueueMetricsResponse>> GetUploadQueueMetricsAsync(
        CancellationToken cancellationToken = default);

    Task<OperateSectionResult<DatabaseResilienceMetricsResponse>> GetDatabaseResilienceMetricsAsync(
        CancellationToken cancellationToken = default);

    Task<OperateSectionResult<ComprehensiveHealthResponse>> GetComprehensiveHealthAsync(
        CancellationToken cancellationToken = default);
}
