using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.IntegrationTests;

internal sealed class FixedServicesDataSource(params OperateServiceDetail[] services) : IOperateTransitionDataSource
{
    public static FixedServicesDataSource WithLayer(OperateServiceLayerProjection layer) =>
        new(new OperateServiceDetail("svc", "Service", "FeatureServer", "running", "server", [layer], [], []));

    public Task<OperateTransitionWorkspace> GetWorkspaceAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new OperateTransitionWorkspace([], [], services, [], []));

    public Task<OperateConnectionSummary?> FindConnectionAsync(string connectionId, CancellationToken cancellationToken = default) =>
        Task.FromResult<OperateConnectionSummary?>(null);

    public Task<OperateResourceEditPreview?> FindResourceEditAsync(string resourceId, CancellationToken cancellationToken = default) =>
        Task.FromResult<OperateResourceEditPreview?>(null);

    public Task<OperateServiceDetail?> FindServiceAsync(string serviceName, CancellationToken cancellationToken = default) =>
        Task.FromResult(services.FirstOrDefault(service =>
            string.Equals(service.Name, serviceName, StringComparison.OrdinalIgnoreCase)));
}
