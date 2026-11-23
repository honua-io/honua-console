using Bunit;
using Honua.Console.Shell.Components.Studio;
using Honua.Console.Shell.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class StyleCatalogListV4Tests
{
    private static readonly StudioMapStyleCatalog Catalog = new(
        Enumerable.Range(1, 30).Select(i => new StudioMapStyleOption($"style-{i:00}", i == 3 ? "Flood zones" : null)).Reverse().ToArray(),
        null, null);

    [Fact]
    public void ListsTheCatalogSortedWithPathsLinksAndPager()
    {
        using var ctx = Context();
        var list = ctx.Render<StyleCatalogListV4>(p => p.Add(c => c.Catalog, Catalog).Add(c => c.Loaded, true));

        Assert.StartsWith("30 styles in the server style catalog", list.Find("[data-style-catalog-summary]").TextContent, StringComparison.Ordinal);
        var rows = list.FindAll("[data-style-row]");
        Assert.Equal(25, rows.Count);
        Assert.Equal("style-01", rows[0].GetAttribute("data-style-row"));
        Assert.Equal("/console/studio/styles/style-01", rows[0].QuerySelector("a")!.GetAttribute("href"));
        Assert.Equal("honua://styles/style-01", rows[0].QuerySelector("code")!.TextContent);
        Assert.Equal("Not set", rows[0].QuerySelector("td")!.TextContent);

        list.Find("[aria-label='Next page']").Click();
        Assert.Equal(5, list.FindAll("[data-style-row]").Count);
    }

    [Fact]
    public void SearchMatchesIdOrTitleAndResetsThePage()
    {
        using var ctx = Context();
        var list = ctx.Render<StyleCatalogListV4>(p => p.Add(c => c.Catalog, Catalog).Add(c => c.Loaded, true));
        list.Find("[aria-label='Next page']").Click();

        list.Find("[data-style-search]").Input("flood");
        var row = Assert.Single(list.FindAll("[data-style-row]"));
        Assert.Equal("style-03", row.GetAttribute("data-style-row"));

        list.Find("[data-style-search]").Input("nothing-like-this");
        Assert.Equal("No styles match this search.", list.Find("[data-style-catalog-empty]").TextContent);
    }

    [Fact]
    public void LoadingFailedAndEmptyCatalogsSaySo()
    {
        using var ctx = Context();
        var loading = ctx.Render<StyleCatalogListV4>(p => p.Add(c => c.Catalog, StudioMapStyleCatalog.Empty));
        Assert.Equal("Reading the style catalog…", loading.Find("[role='status']").TextContent);

        var failed = ctx.Render<StyleCatalogListV4>(p => p
            .Add(c => c.Catalog, new StudioMapStyleCatalog([], null, new("Styles", "Unavailable", "GET /ogc/styles", "The Honua server does not expose the OGC API - Styles list.")))
            .Add(c => c.Loaded, true));
        Assert.Contains("does not expose", failed.Find("[data-style-catalog-issue]").TextContent, StringComparison.Ordinal);
        Assert.StartsWith("The server style catalog (", failed.Find("[data-style-catalog-summary]").TextContent, StringComparison.Ordinal);
        Assert.Empty(failed.FindAll("[data-style-catalog]"));

        var empty = ctx.Render<StyleCatalogListV4>(p => p.Add(c => c.Catalog, StudioMapStyleCatalog.Empty).Add(c => c.Loaded, true));
        Assert.Equal("The style catalog is empty.", empty.Find("[data-style-catalog-empty]").TextContent);
    }

    private static BunitContext Context()
    {
        var ctx = new BunitContext();
        ctx.Services.AddSingleton<Microsoft.AspNetCore.Components.NavigationManager>(new MountedNavigationManager("/console/"));
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        return ctx;
    }
}
