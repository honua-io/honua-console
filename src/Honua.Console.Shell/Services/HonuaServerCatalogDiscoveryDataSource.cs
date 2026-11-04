using System.Globalization;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

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
