using Honua.Console.Contracts;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Server-bound catalog/content client (issue #7, Console Patterns Charter section 11). Binds the
/// Catalog/Studio/Share/Operate-visible content metadata surfaces to honua-server's Console metadata
/// v2 content + RBAC API (honua-server#1162, CLOSED) through the
/// <see cref="IHonuaConsoleContentClient"/> shim. The live binding activates only when a server base
/// URL is configured; with no server configured the shell keeps the
/// <see cref="UnsupportedConsoleCatalogClient"/> missing-binding state instead of fabricating content.
/// </summary>
/// <remarks>
/// honua-server#1162 ships the content item graph and server-authored RBAC verbs, but the public-link
/// token / embed-token resolution and the saved-map package document live behind the separate Console
/// Share facet (ConsoleShareEndpoints) and the content-publication registry (#1183). Until those read
/// paths are projected for anonymous Console viewer reads, the map-package / embed / open-data reads
/// resolve server item metadata and surface a typed unsupported/unavailable state where the package
/// document or anonymous token resolution is not yet server-backed — never mock data.
/// </remarks>
public sealed class HonuaServerConsoleCatalogClient : IConsoleCatalogClient
{
    private readonly IHonuaConsoleContentClient _client;

    public HonuaServerConsoleCatalogClient(IHonuaConsoleContentClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public async Task<CatalogSearchResult> SearchAsync(
        CatalogListRequest request,
        CatalogReadContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var query = ConsoleContentMapper.ToListQuery(request);
        var result = await _client.ListAsync(query, cancellationToken).ConfigureAwait(false);
        if (result.Data is null)
        {
            // A missing-permission/unavailable/unsupported list read surfaces as an empty result set
            // rather than fabricated content; the catalog page renders its empty/blocked state.
            return new CatalogSearchResult([], new Dictionary<string, int>(StringComparer.Ordinal), request);
        }

        var summaries = (result.Data.Items ?? [])
            .Where(item => ConsoleContentMapper.IsVisibleToContext(item, context))
            .Select(ConsoleContentMapper.ToSummary)
            .ToArray();

        var typeCounts = summaries
            .GroupBy(item => item.Type, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        return new CatalogSearchResult(summaries, typeCounts, request);
    }

    public async Task<CatalogItemReadResult> GetCatalogItemAsync(
        string idOrSlug,
        CatalogReadContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idOrSlug);
        ArgumentNullException.ThrowIfNull(context);

        var result = await _client.GetAsync(idOrSlug, cancellationToken).ConfigureAwait(false);
        if (result.Data is null)
        {
            return ToItemFailure(result.Issue);
        }

        if (!ConsoleContentMapper.IsVisibleToContext(result.Data, context))
        {
            return CatalogItemReadResult.Denied(
                CatalogReadStatus.Unavailable,
                "This link is unavailable or no longer grants access.");
        }

        return CatalogItemReadResult.Allowed(ConsoleContentMapper.ToDetail(result.Data), context.Anonymous);
    }

    public async Task<CatalogItemReadResult> GetOpenDataItemAsync(
        string idOrSlug,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idOrSlug);

        var result = await _client.GetAsync(idOrSlug, cancellationToken).ConfigureAwait(false);
        if (result.Data is null)
        {
            return ToItemFailure(result.Issue, "This public open-data item is not available.");
        }

        if (!ConsoleContentMapper.IsOpenDataItem(result.Data))
        {
            return CatalogItemReadResult.Denied(
                CatalogReadStatus.Missing,
                "This public open-data item is not available.");
        }

        return CatalogItemReadResult.Allowed(ConsoleContentMapper.ToDetail(result.Data), anonymousRead: true);
    }

    public async Task<MapPackageReadResult> GetMapPackageAsync(
        string mapId,
        CatalogReadContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mapId);
        ArgumentNullException.ThrowIfNull(context);

        var result = await _client.GetAsync(mapId, cancellationToken).ConfigureAwait(false);
        if (result.Data is null)
        {
            return ToMapFailure(result.Issue, "The requested map was not found.");
        }

        if (!ConsoleContentMapper.IsSavedMap(result.Data))
        {
            return MapPackageReadResult.Denied(
                CatalogReadStatus.Missing,
                "The requested map was not found.");
        }

        if (!ConsoleContentMapper.IsVisibleToContext(result.Data, context))
        {
            return MapPackageReadResult.Denied(
                CatalogReadStatus.Unavailable,
                "This map link is unavailable or no longer grants access.");
        }

        return MapPackageReadResult.Allowed(ConsoleContentMapper.ToMapPackage(result.Data), context.Anonymous);
    }

    public async Task<MapPackageReadResult> GetDraftMapAsync(
        string sourceItemId,
        CatalogReadContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceItemId);
        ArgumentNullException.ThrowIfNull(context);

        if (context.Anonymous)
        {
            return MapPackageReadResult.Denied(
                CatalogReadStatus.Unavailable,
                "Sign in to hydrate a draft map from catalog content.");
        }

        var result = await _client.GetAsync(sourceItemId, cancellationToken).ConfigureAwait(false);
        if (result.Data is null)
        {
            return ToMapFailure(result.Issue, "The source item for this draft map was not found.");
        }

        if (!ConsoleContentMapper.IsDraftMapSource(result.Data))
        {
            return MapPackageReadResult.Denied(
                CatalogReadStatus.UnsupportedServiceMetadata,
                "Draft maps can only be hydrated from supported catalog services or layers.");
        }

        return MapPackageReadResult.Allowed(ConsoleContentMapper.ToDraftMapPackage(result.Data));
    }

    public async Task<MapPackageReadResult> AuthorizeEmbedAsync(
        string mapId,
        EmbedRouteOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mapId);
        ArgumentNullException.ThrowIfNull(options);

        if (options.QueryStringCarriedToken)
        {
            return MapPackageReadResult.Denied(
                CatalogReadStatus.Unavailable,
                "Embed bearer tokens must be supplied in the URL fragment.");
        }

        var result = await _client.GetAsync(mapId, cancellationToken).ConfigureAwait(false);
        if (result.Data is null)
        {
            return ToMapFailure(result.Issue, "The embedded map was not found.");
        }

        if (!ConsoleContentMapper.IsSavedMap(result.Data))
        {
            return MapPackageReadResult.Denied(
                CatalogReadStatus.Missing,
                "The embedded map was not found.");
        }

        // honua-server#1162 surfaces the item's embed RBAC verb; the anonymous embed-token resolution
        // path lives behind the Console Share facet (ConsoleShareEndpoints embed tokens) and is not yet
        // projected for anonymous Console viewer reads. Surface a typed unavailable state for non-public
        // maps rather than fabricating a token check.
        if (!ConsoleContentMapper.HasEmbedAction(result.Data))
        {
            return MapPackageReadResult.Denied(
                CatalogReadStatus.Unavailable,
                "This map is not embeddable.");
        }

        if (!ConsoleContentMapper.IsPublic(result.Data))
        {
            return MapPackageReadResult.Denied(
                CatalogReadStatus.UnsupportedPackageBinding,
                "Anonymous embed-token resolution is served by the Console Share facet, which is not yet "
                + "bound for anonymous viewer reads in Console.");
        }

        return MapPackageReadResult.Allowed(ConsoleContentMapper.ToMapPackage(result.Data), anonymousRead: true);
    }

    private static CatalogItemReadResult ToItemFailure(HonuaAdminEndpointIssue? issue, string? notFoundMessage = null)
    {
        var status = ConsoleContentMapper.ToReadStatus(issue);
        var message = status == CatalogReadStatus.Missing
            ? notFoundMessage ?? "The requested catalog item was not found."
            : issue?.Detail ?? UnsupportedConsoleCatalogClient.MissingBindingMessage;
        return CatalogItemReadResult.Denied(status, message);
    }

    private static MapPackageReadResult ToMapFailure(HonuaAdminEndpointIssue? issue, string notFoundMessage)
    {
        var status = ConsoleContentMapper.ToReadStatus(issue);
        var message = status == CatalogReadStatus.Missing
            ? notFoundMessage
            : issue?.Detail ?? UnsupportedConsoleCatalogClient.MissingBindingMessage;
        return MapPackageReadResult.Denied(status, message);
    }
}
