using System.Globalization;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Operate &gt; Catalogs data source bound to the honua-server catalog discovery-endpoints registry
/// (honua-server#1279) through the <see cref="IHonuaCatalogDiscoveryClient"/> shim. Every read speaks the
/// live registry surface (<c>/api/v1/console/catalog-endpoints/...</c>); there is no in-memory discovery
/// data in the merged result (Console Patterns Charter section 11). Endpoint issues (missing permission, not
/// found, unsupported, transport) surface as explicit capability states instead of throwing or fabricating
/// endpoint/item data.
/// </summary>
public sealed class HonuaServerCatalogDiscoveryDataSource : ICatalogDiscoveryDataSource
{
    private const string Surface = "Catalogs (discovery endpoints)";

    private readonly IHonuaCatalogDiscoveryClient _client;

    public HonuaServerCatalogDiscoveryDataSource(IHonuaCatalogDiscoveryClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public async Task<CatalogDiscoveryRegistryLoad> LoadRegistryAsync(string workspaceId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);

        var result = await _client.GetRegistryAsync(workspaceId, cancellationToken).ConfigureAwait(false);
        if (result.Issue is { } issue)
        {
            return new CatalogDiscoveryRegistryLoad(null, [ToCapabilityState(issue)]);
        }

        return new CatalogDiscoveryRegistryLoad(CatalogDiscoveryMapper.ToView(result.Data!), []);
    }

    public async Task<CatalogEndpointDetailLoad> LoadEndpointAsync(string workspaceId, string endpointKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(endpointKey);

        var result = await _client.GetEndpointAsync(workspaceId, endpointKey, cancellationToken).ConfigureAwait(false);
        if (result.Issue is { } issue)
        {
            return new CatalogEndpointDetailLoad(null, [ToCapabilityState(issue)]);
        }

        return new CatalogEndpointDetailLoad(CatalogDiscoveryMapper.ToView(result.Data!), []);
    }

    public async Task<CatalogItemLoad> LoadItemAsync(string workspaceId, string endpointKey, string itemId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(endpointKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);

        var result = await _client.GetItemAsync(workspaceId, endpointKey, itemId, cancellationToken).ConfigureAwait(false);
        if (result.Issue is { } issue)
        {
            return new CatalogItemLoad(null, [ToCapabilityState(issue)]);
        }

        return new CatalogItemLoad(CatalogDiscoveryMapper.ToView(result.Data!), []);
    }

    private static CatalogDiscoveryCapabilityState ToCapabilityState(HonuaAdminEndpointIssue issue) =>
        new(
            Surface,
            issue.State,
            issue.Contract,
            issue.StatusCode is null
                ? issue.Detail
                : $"{issue.Detail} HTTP {issue.StatusCode.Value.ToString(CultureInfo.InvariantCulture)}.");
}
