using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

// With no server base URL the unsupported implementation returns missing-binding and does not invent endpoints or items.
public interface ICatalogDiscoveryDataSource
{
    Task<CatalogDiscoveryRegistryLoad> LoadRegistryAsync(string workspaceId, CancellationToken cancellationToken = default);

    Task<CatalogEndpointDetailLoad> LoadEndpointAsync(string workspaceId, string endpointKey, CancellationToken cancellationToken = default);

    Task<CatalogItemLoad> LoadItemAsync(string workspaceId, string endpointKey, string itemId, CancellationToken cancellationToken = default);
}
