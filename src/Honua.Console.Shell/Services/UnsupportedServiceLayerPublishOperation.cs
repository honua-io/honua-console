using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public sealed class UnsupportedServiceLayerPublishOperation : IServiceLayerPublishOperation
{
    public Task<ServiceLayerPublishResult> PublishAsync(
        ServiceLayerPublishCommand command,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(ServiceLayerPublishResult.MissingBinding(
            "Configure Honua:Server:BaseUrl or HONUA_SERVER_BASE_URL so the console can publish layers to honua-server."));

    public Task<IReadOnlyList<ServiceLayerPublishTable>> ListTablesAsync(
        string connectionId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ServiceLayerPublishTable>>([]);
}
