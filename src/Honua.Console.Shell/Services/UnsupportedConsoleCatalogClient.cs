using Honua.Console.Contracts;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Catalog/content client used when no honua-server base address is configured. Every read returns an
/// explicit missing-binding state instead of fabricating catalog content, keeping the merged runtime free
/// of a standing in-memory catalog source (issue #7, Console Patterns Charter). Catalog, Studio, Share, and
/// Operate-visible content metadata stays bound to a real honua-server (via <c>Honua:Server:BaseUrl</c> /
/// <c>HONUA_SERVER_BASE_URL</c>) or surfaces this state. Mirrors
/// <see cref="UnsupportedStudioFormPackageDataSource"/> and <see cref="UnsupportedOperateTransitionDataSource"/>.
/// </summary>
public sealed class UnsupportedConsoleCatalogClient : IConsoleCatalogClient
{
    /// <summary>
    /// The server contract the catalog/content surfaces require before they can render live metadata.
    /// </summary>
    public const string MissingBindingContract = "Honua:Server:BaseUrl";

    internal const string MissingBindingMessage =
        "Configure Honua:Server:BaseUrl or HONUA_SERVER_BASE_URL so Console can bind the server-owned "
        + "catalog/content metadata projection from honua-server.";

    public Task<CatalogSearchResult> SearchAsync(
        CatalogListRequest request,
        CatalogReadContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(new CatalogSearchResult(
            [],
            new Dictionary<string, int>(StringComparer.Ordinal),
            request));
    }

    public Task<CatalogItemReadResult> GetCatalogItemAsync(
        string idOrSlug,
        CatalogReadContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idOrSlug);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(MissingItem());
    }

    public Task<CatalogItemReadResult> GetOpenDataItemAsync(
        string idOrSlug,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idOrSlug);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(MissingItem());
    }

    public Task<MapPackageReadResult> GetMapPackageAsync(
        string mapId,
        CatalogReadContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mapId);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(MissingMapPackage());
    }

    public Task<MapPackageReadResult> GetDraftMapAsync(
        string sourceItemId,
        CatalogReadContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceItemId);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(MissingMapPackage());
    }

    public Task<MapPackageReadResult> AuthorizeEmbedAsync(
        string mapId,
        EmbedRouteOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mapId);
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(MissingMapPackage());
    }

    private static CatalogItemReadResult MissingItem() =>
        CatalogItemReadResult.Denied(CatalogReadStatus.Unavailable, MissingBindingMessage);

    private static MapPackageReadResult MissingMapPackage() =>
        MapPackageReadResult.Denied(CatalogReadStatus.Unavailable, MissingBindingMessage);
}
