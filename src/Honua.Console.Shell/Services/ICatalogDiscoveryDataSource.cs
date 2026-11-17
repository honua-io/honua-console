using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

// With no server base URL the unsupported implementation returns missing-binding and does not invent endpoints or items.
public interface ICatalogDiscoveryDataSource
{
    Task<CatalogDiscoveryRegistryLoad> LoadRegistryAsync(string environmentId, CancellationToken cancellationToken = default);

    Task<CatalogEndpointDetailLoad> LoadEndpointAsync(string environmentId, string endpointKey, CancellationToken cancellationToken = default);

    Task<CatalogItemLoad> LoadItemAsync(string environmentId, string endpointKey, string itemId, CancellationToken cancellationToken = default);
}
