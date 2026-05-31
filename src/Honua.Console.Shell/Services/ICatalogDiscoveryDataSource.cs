using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Operate &gt; Catalogs data source. Binds the discovery-endpoints surfaces — the CatalogsList registry, the
/// per-endpoint EsriCatalogDetail drill-down, and the CatalogItemEditor — to the honua-server catalog
/// discovery-endpoints registry (honua-server#1279) through the Honua.Console.Contracts shim. There is no
/// standing in-memory discovery client in the merged result (Console Patterns Charter section 11): when no
/// server base URL is configured, the unsupported implementation surfaces an explicit missing-binding state
/// rather than fabricating endpoint/item data.
/// </summary>
public interface ICatalogDiscoveryDataSource
{
    /// <summary>Loads the server-owned discovery-endpoints registry, or capability states when it cannot be read.</summary>
    Task<CatalogDiscoveryRegistryLoad> LoadRegistryAsync(string workspaceId, CancellationToken cancellationToken = default);

    /// <summary>Loads one endpoint with its mirrored items, or capability states when it cannot be read.</summary>
    Task<CatalogEndpointDetailLoad> LoadEndpointAsync(string workspaceId, string endpointKey, CancellationToken cancellationToken = default);

    /// <summary>Loads one catalog item with its field groups, or capability states when it cannot be read.</summary>
    Task<CatalogItemLoad> LoadItemAsync(string workspaceId, string endpointKey, string itemId, CancellationToken cancellationToken = default);
}
