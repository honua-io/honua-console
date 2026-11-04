using Honua.Console.Contracts;

namespace Honua.Console.Shell.Services;

// Monitoring responses are bare JSON with no ApiResponse envelope.
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
