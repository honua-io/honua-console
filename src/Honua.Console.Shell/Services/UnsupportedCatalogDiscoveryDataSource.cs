using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public sealed class UnsupportedCatalogDiscoveryDataSource : ICatalogDiscoveryDataSource
{
    private const string Surface = "Catalogs (discovery endpoints)";

    private static readonly CatalogDiscoveryCapabilityState MissingBinding = new(
        Surface,
        "Missing binding",
        "Honua:Server:BaseUrl",
        "Configure Honua:Server:BaseUrl or HONUA_SERVER_BASE_URL so the Catalogs surface can bind the server-owned catalog discovery-endpoints registry (honua-server#1279). honua-server does not yet expose this contract.");

    public Task<CatalogDiscoveryRegistryLoad> LoadRegistryAsync(string workspaceId, CancellationToken cancellationToken = default) =>
        Task.FromResult(new CatalogDiscoveryRegistryLoad(null, [MissingBinding]));

    public Task<CatalogEndpointDetailLoad> LoadEndpointAsync(string workspaceId, string endpointKey, CancellationToken cancellationToken = default) =>
        Task.FromResult(new CatalogEndpointDetailLoad(null, [MissingBinding]));

    public Task<CatalogItemLoad> LoadItemAsync(string workspaceId, string endpointKey, string itemId, CancellationToken cancellationToken = default) =>
        Task.FromResult(new CatalogItemLoad(null, [MissingBinding]));
}
