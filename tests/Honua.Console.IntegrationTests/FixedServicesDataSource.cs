using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.IntegrationTests;

internal sealed class FixedServicesDataSource(params OperateServiceDetail[] services) : IOperateTransitionDataSource
{
    public static FixedServicesDataSource WithLayer(OperateServiceLayer layer) =>
        new(new OperateServiceDetail("svc", "Service", "FeatureServer", "running", "server", [layer], [], []));

    public Task<OperateEnvironmentSnapshot> GetEnvironmentAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new OperateEnvironmentSnapshot([], [], services, [], []));

    public Task<OperateConnectionSummary?> FindConnectionAsync(string connectionId, CancellationToken cancellationToken = default) =>
        Task.FromResult<OperateConnectionSummary?>(null);

    public Task<OperateDatasetEditPreview?> FindDatasetEditAsync(string resourceId, CancellationToken cancellationToken = default) =>
        Task.FromResult<OperateDatasetEditPreview?>(null);

    public Task<OperateServiceDetail?> FindServiceAsync(string serviceName, CancellationToken cancellationToken = default) =>
        Task.FromResult(services.FirstOrDefault(service =>
            string.Equals(service.Name, serviceName, StringComparison.OrdinalIgnoreCase)));
}
