using System.Net;
using Bunit;
using Honua.Console.Contracts;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class PublicCatalogV4Tests
{
    private static readonly DateTimeOffset BaseTime = new(2026, 10, 21, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SearchMatchesTitleSummaryAndTagsAndProvidesRemovableChip()
    {
        using var context = new BunitContext();
        var view = context.Render<PublicCatalogView>(p => p.Add(v => v.Items, SampleItems()));

        view.Find("#public-catalog-search").Input("shelters");
        view.Find("form[role='search']").Submit();

        Assert.Equal("1 results", view.Find(".public-catalog-toolbar h2").TextContent);
        Assert.Equal(["Emergency shelters"], view.FindAll(".public-catalog-card h3 a").Select(n => n.TextContent));
        var chip = view.Find("button[aria-label='Remove search filter']");
        Assert.Contains("shelters", chip.TextContent);

        chip.Click();
        Assert.Equal("3 results", view.Find(".public-catalog-toolbar h2").TextContent);
        Assert.Empty(view.FindAll(".public-catalog-applied-chip"));
    }

    [Fact]
    public void FacetsFilterByKindAndFormatAndClearAllResetsActiveFilters()
    {
        using var context = new BunitContext();
        var view = context.Render<PublicCatalogView>(p => p.Add(v => v.Items, SampleItems()));

        view.FindAll(".public-catalog-facet-btn").First(b => b.TextContent.Contains("Service", StringComparison.Ordinal)).Click();
        Assert.Equal("1 results", view.Find(".public-catalog-toolbar h2").TextContent);
        Assert.Single(view.FindAll(".public-catalog-applied-chip"));

        view.FindAll(".public-catalog-facet-btn").First(b => b.TextContent.Contains("CSV", StringComparison.Ordinal)).Click();
        Assert.Equal("0 results", view.Find(".public-catalog-toolbar h2").TextContent);
        Assert.Contains("No matching public items", view.Markup);
        Assert.Equal(2, view.FindAll(".public-catalog-applied-chip").Count);

        view.Find(".public-catalog-clear-btn").Click();
        Assert.Equal("3 results", view.Find(".public-catalog-toolbar h2").TextContent);
        Assert.Empty(view.FindAll(".public-catalog-applied-chip"));
    }

    [Fact]
    public void SortSwitchesBetweenRecentlyUpdatedAndTitle()
    {
        using var context = new BunitContext();
        var view = context.Render<PublicCatalogView>(p => p.Add(v => v.Items, SampleItems()));

        Assert.Equal(
            ["Watershed report", "Emergency shelters", "Flood zones"],
            view.FindAll(".public-catalog-card h3 a").Select(n => n.TextContent));

        view.Find(".public-catalog-sort select").Change("title");

        Assert.Equal(
            ["Emergency shelters", "Flood zones", "Watershed report"],
            view.FindAll(".public-catalog-card h3 a").Select(n => n.TextContent));
    }

    [Fact]
    public void PaginationRendersNumberedPagesAndResetsWhenFiltering()
    {
        using var context = new BunitContext();
        var items = Enumerable.Range(1, 15)
            .Select(i => new ConsoleContentSummary
            {
                Id = $"item-{i:00}",
                Type = i <= 13 ? "service" : "layer",
                Title = $"Dataset {i:00}",
                Summary = $"Public dataset {i:00}.",
                Owner = "Open GIS",
                Modified = BaseTime.AddDays(i)
            })
            .ToArray();

        var view = context.Render<PublicCatalogView>(p => p.Add(v => v.Items, items));

        Assert.Equal(12, view.FindAll(".public-catalog-card").Count);
        Assert.Equal("1", view.Find(".public-catalog-page-num[aria-current='page']").TextContent);
        Assert.Equal("Page 1 of 2", view.Find(".public-catalog-pager-status").TextContent);

        view.Find("button[aria-label='Page 2']").Click();
        Assert.Equal(3, view.FindAll(".public-catalog-card").Count);
        Assert.Equal("2", view.Find(".public-catalog-page-num[aria-current='page']").TextContent);

        view.FindAll(".public-catalog-facet-btn").First(b => b.TextContent.Contains("Layer", StringComparison.Ordinal)).Click();
        Assert.Equal(2, view.FindAll(".public-catalog-card").Count);
        Assert.Empty(view.FindAll(".public-catalog-pager"));
    }

    [Fact]
    public void EmptyCatalogOmitsFacetSidebarAndRendersEmptyState()
    {
        using var context = new BunitContext();
        var view = context.Render<PublicCatalogView>(p => p
            .Add(v => v.Items, Array.Empty<ConsoleContentSummary>())
            .Add(v => v.HasMoreItems, true));

        Assert.Empty(view.FindAll(".public-catalog-facets"));
        Assert.Contains("No public open-data items", view.Markup);
        Assert.Contains("The server has more public items.", view.Markup);
    }

    [Fact]
    public void SharePageV4UsesPublicBrandHeaderForMissingBindingAndErrorStates()
    {
        using var missingContext = CreateShareContext(new UnsupportedConsoleCatalogClient());
        var missingPage = missingContext.Render<SharePage>();

        Assert.Equal("Open data catalog", missingPage.Find(".public-catalog-brand strong").TextContent);
        Assert.Contains("Share is not bound to honua-server", missingPage.Markup);
        Assert.Empty(missingPage.FindAll(".console-page-heading-v4"));

        using var failingContext = CreateShareContext(new FailingOpenDataCatalogClient());
        var failingPage = failingContext.Render<SharePage>();

        Assert.Equal("Open data catalog", failingPage.Find(".public-catalog-brand strong").TextContent);
        Assert.Contains("Public items could not load", failingPage.Markup);
        Assert.Contains("Retry", failingPage.Markup);
    }

    [Fact]
    public void SharePublicItemV4RendersViewerActionUsageAndMultipleRoutes()
    {
        var detail = CreateRichPublicMapItem();
        using var context = CreateShareContext(new SingleItemCatalogClient(detail));

        var page = context.Render<SharePublicItemPage>(p => p.Add(x => x.IdOrSlug, detail.Summary.Id));

        Assert.Equal("Open in map viewer", page.Find(".public-item-actions a.console-button-primary").TextContent.Trim());
        Assert.Equal("/maps/evacuation-map", page.Find(".public-item-actions a.console-button-primary").GetAttribute("href"));
        Assert.Equal("2 routes", page.Find("[data-share-opendata-routes] .public-item-panel-count").TextContent);
        Assert.Equal(2, page.FindAll("[data-share-opendata-routes] button[data-console-copy]").Count);
        Assert.Equal("2 formats", page.Find("[data-share-opendata-downloads] .public-item-panel-count").TextContent);
        Assert.Equal("Evacuation routes and shelter locations.", page.Find(".share-opendata-intro").TextContent);
        Assert.Contains("Shelter Finder", page.Find(".public-item-usage").TextContent);
        Assert.Contains("CC BY 4.0", page.Find(".public-item-kv").TextContent);
        Assert.Contains("No tags published.", page.Markup);
    }

    private static BunitContext CreateShareContext(IConsoleCatalogClient catalog)
    {
        var context = new BunitContext();
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = true });
        context.Services.AddSingleton(catalog);
        context.Services.AddSingleton(ConsoleCapabilityTestManifest.All);
        return context;
    }

    private static ConsoleContentSummary[] SampleItems() =>
    [
        new() { Id = "flood-service", Type = "service", Title = "Flood zones", Summary = "County flood hazard areas.", Owner = "Emergency GIS", Tags = ["flood"], Formats = ["OGC:API:Features"], Modified = BaseTime },
        new() { Id = "shelters-layer", Type = "layer", Title = "Emergency shelters", Summary = "Designated shelter locations.", Owner = "Emergency GIS", Tags = ["shelters"], Formats = ["CSV"], Modified = BaseTime.AddDays(1) },
        new() { Id = "watershed-doc", Type = "document", Title = "Watershed report", Summary = "Annual watershed outlook.", Owner = "Hydrology", Tags = ["water"], Formats = ["PDF"], Modified = BaseTime.AddDays(2) }
    ];

    private static ConsoleContentDetail CreateRichPublicMapItem() => new()
    {
        Summary = new ConsoleContentSummary
        {
            Id = "evacuation-map",
            Slug = "evacuation-map",
            Type = "map",
            Title = "Evacuation Map",
            Summary = "Evacuation routes and shelter locations.",
            Owner = "Emergency Operations GIS",
            Formats = ["OGC:API:Features", "GeoServices:MapServer"],
            Modified = BaseTime,
            Access = new ConsoleShareAccess { Sharing = CatalogSharingTiers.Public, OpenData = true },
            ViewerSupport = new ConsoleViewerSupport { CanOpenInViewer = true, SupportState = ConsoleContentSupportState.Supported }
        },
        Description = string.Empty,
        License = "CC BY 4.0",
        Publications =
        [
            new ConsoleContentPublication("https://data.example.org/ogc/evacuation", CatalogSharingTiers.Public, false, "published") { Format = "OGC:API:Features" },
            new ConsoleContentPublication("/rest/services/evacuation/MapServer", CatalogSharingTiers.Public, false, "published") { Format = "GeoServices:MapServer" }
        ],
        Usage =
        [
            new ConsoleContentUsage("app-shelter-finder", "Shelter Finder", "app", "low")
        ]
    };

    private sealed class FailingOpenDataCatalogClient : IConsoleCatalogClient
    {
        public Task<CatalogSearchResult> SearchAsync(CatalogListRequest request, CatalogReadContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(CatalogSearchResult.Failed(request, CatalogReadStatus.Unavailable, "Service unavailable."));

        public Task<CatalogSearchResult> ListOpenDataItemsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CatalogSearchResult.Failed(new CatalogListRequest { Sharing = CatalogSharingTiers.Public }, CatalogReadStatus.Unavailable, $"Server returned {(int)HttpStatusCode.ServiceUnavailable}."));

        public Task<CatalogItemReadResult> GetCatalogItemAsync(string idOrSlug, CatalogReadContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(CatalogItemReadResult.Denied(CatalogReadStatus.Unavailable, "Unavailable."));

        public Task<CatalogItemReadResult> GetOpenDataItemAsync(string idOrSlug, CancellationToken cancellationToken = default) =>
            Task.FromResult(CatalogItemReadResult.Denied(CatalogReadStatus.Unavailable, "Unavailable."));

        public Task<MapReadResult> GetMapAsync(string mapId, CatalogReadContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(MapReadResult.Denied(CatalogReadStatus.Unavailable, "Unavailable."));

        public Task<MapReadResult> GetDraftMapAsync(string sourceItemId, CatalogReadContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(MapReadResult.Denied(CatalogReadStatus.Unavailable, "Unavailable."));

        public Task<MapReadResult> AuthorizeEmbedAsync(string mapId, EmbedRouteOptions options, CancellationToken cancellationToken = default) =>
            Task.FromResult(MapReadResult.Denied(CatalogReadStatus.Unavailable, "Unavailable."));
    }

    private sealed class SingleItemCatalogClient(ConsoleContentDetail detail) : IConsoleCatalogClient
    {
        public Task<CatalogSearchResult> SearchAsync(CatalogListRequest request, CatalogReadContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CatalogSearchResult([detail.Summary], new Dictionary<string, int> { [detail.Summary.Type] = 1 }, request));

        public Task<CatalogSearchResult> ListOpenDataItemsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new CatalogSearchResult([detail.Summary], new Dictionary<string, int> { [detail.Summary.Type] = 1 }, new CatalogListRequest { Sharing = CatalogSharingTiers.Public }));

        public Task<CatalogItemReadResult> GetCatalogItemAsync(string idOrSlug, CatalogReadContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(CatalogItemReadResult.Allowed(detail, context.Anonymous));

        public Task<CatalogItemReadResult> GetOpenDataItemAsync(string idOrSlug, CancellationToken cancellationToken = default) =>
            Task.FromResult(CatalogItemReadResult.Allowed(detail, anonymousRead: true));

        public Task<MapReadResult> GetMapAsync(string mapId, CatalogReadContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(MapReadResult.Denied(CatalogReadStatus.Missing, "Missing."));

        public Task<MapReadResult> GetDraftMapAsync(string sourceItemId, CatalogReadContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(MapReadResult.Denied(CatalogReadStatus.Missing, "Missing."));

        public Task<MapReadResult> AuthorizeEmbedAsync(string mapId, EmbedRouteOptions options, CancellationToken cancellationToken = default) =>
            Task.FromResult(MapReadResult.Denied(CatalogReadStatus.Missing, "Missing."));
    }
}
