using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Operate &gt; Catalogs data source used when no honua-server base address is configured. It renders an
/// explicit missing-binding state instead of fabricating endpoint/item data, keeping the merged runtime free
/// of a standing in-memory catalog discovery client (Console Patterns Charter section 11). The Catalogs
/// surfaces stay bound to the real honua-server catalog discovery-endpoints registry (honua-server#1279) or
/// show this state.
/// </summary>
public sealed class UnsupportedCatalogDiscoveryDataSource : ICatalogDiscoveryDataSource
{
    private const string Surface = "Catalogs (discovery endpoints)";

    private static readonly CatalogDiscoveryCapabilityState MissingBinding = new(
        Surface,
        "Missing binding",
        "Honua:Server:BaseUrl",
        "Configure Honua:Server:BaseUrl or HONUA_SERVER_BASE_URL so the Catalogs surface can bind the server-owned catalog discovery-endpoints registry (honua-server#1279).");

    public Task<CatalogDiscoveryRegistryLoad> LoadRegistryAsync(string workspaceId, CancellationToken cancellationToken = default) =>
        Task.FromResult(new CatalogDiscoveryRegistryLoad(null, [MissingBinding]));

    public Task<CatalogEndpointDetailLoad> LoadEndpointAsync(string workspaceId, string endpointKey, CancellationToken cancellationToken = default) =>
        Task.FromResult(new CatalogEndpointDetailLoad(null, [MissingBinding]));

    public Task<CatalogItemLoad> LoadItemAsync(string workspaceId, string endpointKey, string itemId, CancellationToken cancellationToken = default) =>
        Task.FromResult(new CatalogItemLoad(null, [MissingBinding]));
}
