using System.Globalization;

namespace Honua.Console.Contracts;

public readonly record struct HonuaAdminCatalogQuery(int Page, int PageSize, string? NameQuery)
{
    public const int MaxPageSize = 100;

    public static HonuaAdminCatalogQuery First { get; } = new(1, MaxPageSize, null);

    public HonuaAdminCatalogQuery Next() => this with { Page = Page + 1 };

    public string AppendTo(string path, string? serviceName = null)
    {
        var parts = new List<string>
        {
            "page=" + Page.ToString(CultureInfo.InvariantCulture),
            "pageSize=" + PageSize.ToString(CultureInfo.InvariantCulture),
        };
        if (!string.IsNullOrWhiteSpace(serviceName))
        {
            parts.Add("serviceName=" + Uri.EscapeDataString(serviceName));
        }

        if (!string.IsNullOrWhiteSpace(NameQuery))
        {
            parts.Add("q=" + Uri.EscapeDataString(NameQuery.Trim()));
        }

        return path + "?" + string.Join('&', parts);
    }
}

public readonly record struct HonuaAdminLayerListRequest(string? ServiceName, HonuaAdminCatalogQuery Page);

public sealed record HonuaAdminServicePage
{
    public HonuaAdminServiceSummary[] Items { get; init; } = [];

    public int Page { get; init; }

    public int PageSize { get; init; }

    public int Total { get; init; }

    public int LayerTotal { get; init; }
}

public sealed record HonuaAdminLayerPage
{
    public HonuaAdminPublishedLayerSummary[] Items { get; init; } = [];

    public int Page { get; init; }

    public int PageSize { get; init; }

    public int Total { get; init; }
}

public static class HonuaAdminCatalogReader
{
    public static async Task<HonuaAdminEndpointResult<IReadOnlyList<TItem>>> ReadAllAsync<TPage, TItem>(
        Func<HonuaAdminCatalogQuery, CancellationToken, Task<HonuaAdminEndpointResult<TPage>>> read,
        Func<TPage, IReadOnlyList<TItem>?> items,
        Func<TPage, int> total,
        CancellationToken cancellationToken)
    {
        var collected = new List<TItem>();
        var query = HonuaAdminCatalogQuery.First;
        while (true)
        {
            var result = await read(query, cancellationToken).ConfigureAwait(false);
            if (result.Issue is { } issue)
            {
                return HonuaAdminEndpointResult<IReadOnlyList<TItem>>.FromIssue(issue);
            }

            if (result.Data is null)
            {
                return HonuaAdminEndpointResult<IReadOnlyList<TItem>>.FromIssue(new HonuaAdminEndpointIssue(
                    "Unavailable",
                    "catalog-page",
                    "The server did not return a catalog page."));
            }

            var pageItems = items(result.Data) ?? [];
            collected.AddRange(pageItems);
            var matchTotal = total(result.Data);
            var pageCount = query.PageSize <= 0 ? 1 : Math.Max(1, (matchTotal + query.PageSize - 1) / query.PageSize);
            if (pageItems.Count == 0 || collected.Count >= matchTotal || query.Page >= pageCount)
            {
                return HonuaAdminEndpointResult<IReadOnlyList<TItem>>.FromData(collected);
            }

            query = query.Next();
        }
    }
}
