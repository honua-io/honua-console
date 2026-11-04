using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public interface IServiceLayerPublishOperation
{
    Task<ServiceLayerPublishResult> PublishAsync(
        ServiceLayerPublishCommand command,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ServiceLayerPublishTable>> ListTablesAsync(
        string connectionId,
        CancellationToken cancellationToken = default);
}
