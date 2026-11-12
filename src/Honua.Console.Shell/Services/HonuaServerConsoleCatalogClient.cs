using Honua.Console.Contracts;

namespace Honua.Console.Shell.Services;

// Map-package, embed, and open-data reads surface unsupported or unavailable when the package document or anonymous token is not server-backed, and they do not substitute mock data.
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
            // Forward the server issue as a TYPED failure instead of collapsing to an empty result — an empty
            // CatalogSearchResult here is indistinguishable from a genuine empty catalog and would hide a live
            // server outage/timeout/permission failure behind the "publish the first item" empty state
            // (issue #272). Mirrors GetCatalogItemAsync/ToItemFailure. The catalog page renders a distinct
            // error/retry state for a failed read; never fabricated content.
            return ToSearchFailure(result.Issue, request);
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

    public async Task<MapReadResult> GetMapAsync(
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
            return MapReadResult.Denied(
                CatalogReadStatus.Missing,
                "The requested map was not found.");
        }

        if (!ConsoleContentMapper.IsVisibleToContext(result.Data, context))
        {
            return MapReadResult.Denied(
                CatalogReadStatus.Unavailable,
                "This map link is unavailable or no longer grants access.");
        }

        return MapReadResult.Allowed(ConsoleContentMapper.ToMap(result.Data), context.Anonymous);
    }

    public async Task<MapReadResult> GetDraftMapAsync(
        string sourceItemId,
        CatalogReadContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceItemId);
        ArgumentNullException.ThrowIfNull(context);

        if (context.Anonymous)
        {
            return MapReadResult.Denied(
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
            return MapReadResult.Denied(
                CatalogReadStatus.UnsupportedServiceMetadata,
                "Draft maps can only be hydrated from supported catalog services or layers.");
        }

        return MapReadResult.Allowed(ConsoleContentMapper.ToDraftMap(result.Data));
    }

    public async Task<MapReadResult> AuthorizeEmbedAsync(
        string mapId,
        EmbedRouteOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mapId);
        ArgumentNullException.ThrowIfNull(options);

        if (options.QueryStringCarriedToken)
        {
            return MapReadResult.Denied(
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
            return MapReadResult.Denied(
                CatalogReadStatus.Missing,
                "The embedded map was not found.");
        }

        // honua-server#1162 surfaces the item's embed RBAC verb; the anonymous embed-token resolution
        // path lives behind the Console Share facet (ConsoleShareEndpoints embed tokens) and is not yet
        // projected for anonymous Console viewer reads. Surface a typed unavailable state for non-public
        // maps rather than fabricating a token check.
        if (!ConsoleContentMapper.HasEmbedAction(result.Data))
        {
            return MapReadResult.Denied(
                CatalogReadStatus.Unavailable,
                "This map is not embeddable.");
        }

        if (!ConsoleContentMapper.IsPublic(result.Data))
        {
            return MapReadResult.Denied(
                CatalogReadStatus.UnsupportedContentBinding,
                "Anonymous embed-token resolution is served by the Console Share facet, which is not yet "
                + "bound for anonymous viewer reads in Console.");
        }

        return MapReadResult.Allowed(ConsoleContentMapper.ToMap(result.Data), anonymousRead: true);
    }

    private static CatalogSearchResult ToSearchFailure(HonuaAdminEndpointIssue? issue, CatalogListRequest request)
    {
        var status = ConsoleContentMapper.ToReadStatus(issue);
        var message = issue?.Detail ?? UnsupportedConsoleCatalogClient.MissingBindingMessage;
        return CatalogSearchResult.Failed(request, status, message);
    }

    private static CatalogItemReadResult ToItemFailure(HonuaAdminEndpointIssue? issue, string? notFoundMessage = null)
    {
        var status = ConsoleContentMapper.ToReadStatus(issue);
        var message = status == CatalogReadStatus.Missing
            ? notFoundMessage ?? "The requested catalog item was not found."
            : issue?.Detail ?? UnsupportedConsoleCatalogClient.MissingBindingMessage;
        return CatalogItemReadResult.Denied(status, message);
    }

    private static MapReadResult ToMapFailure(HonuaAdminEndpointIssue? issue, string notFoundMessage)
    {
        var status = ConsoleContentMapper.ToReadStatus(issue);
        var message = status == CatalogReadStatus.Missing
            ? notFoundMessage
            : issue?.Detail ?? UnsupportedConsoleCatalogClient.MissingBindingMessage;
        return MapReadResult.Denied(status, message);
    }
}
