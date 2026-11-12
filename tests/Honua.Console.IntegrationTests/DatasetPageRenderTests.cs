using Bunit;
using Honua.Console.Shell.Components.Operate;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class DatasetPageRenderTests
{
    [Fact]
    public async Task Datasets_LegacyTreeSelectionAndQueryChangesUseCanonicalResource()
    {
        var (context, registry) = CreateContext(new ConsoleV4Options { Enabled = false });
        using var ctx = context;
        var navigation = ctx.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/console/operate/data?focus=parcels");
        var page = ctx.Render<OperateDataPage>();
        page.WaitForAssertion(() => Assert.Single(page.FindAll("[data-resource-tree]")));
        Assert.Empty(page.FindAll(".console-page-heading-v4"));
        Assert.Empty(page.FindAll("[data-layer-preview-catalog]"));
        Assert.Equal("/console/operate/data/new", page.Find("a.console-button").GetAttribute("href"));
        Assert.Contains("parcels", page.Find("[data-layer-preview] h2").TextContent);
        await page.InvokeAsync(() => navigation.NavigateTo("/console/operate/data?focus=draft"));
        page.WaitForAssertion(() => Assert.Single(page.FindAll("[data-preview-empty]")));
        Assert.Contains("in the tree", page.Find("[data-preview-empty]").TextContent);
        page.Find("[data-resource-node='parcels'] .resource-tree__name").Click();
        Assert.Equal(0, registry.Current.Unsaved.PendingOperationCount);
    }

    [Fact]
    public void Datasets_V4CatalogListsEveryDatasetWithTypeRoutesAndPublishActions()
    {
        var (context, registry) = CreateContext(new ConsoleV4Options { Enabled = true });
        using var ctx = context;
        ctx.Services.GetRequiredService<NavigationManager>().NavigateTo("/console/operate/data?focus=parcels");
        var page = ctx.Render<OperateDataPage>();
        page.WaitForAssertion(() => Assert.Single(page.FindAll("[data-layer-preview-catalog]")));

        Assert.Empty(page.FindAll("[data-resource-tree]"));
        Assert.Equal("Layer preview", page.Find(".console-page-heading-v4 h1").TextContent);
        Assert.Equal("/console/operate/data/new", page.Find(".console-page-heading-v4 a.console-button").GetAttribute("href"));
        Assert.Contains("parcels", page.Find("[data-layer-preview] h2").TextContent);
        var parcels = page.Find("tr[data-resource-node='parcels']");
        Assert.Equal("true", parcels.GetAttribute("aria-current"));
        Assert.Equal("Polygon", parcels.QuerySelector("[data-catalog-row-type]")!.TextContent);
        Assert.NotEmpty(parcels.QuerySelectorAll("[data-catalog-route]"));
        Assert.Equal("/console/operate/data/new?source=existingresource&resource=parcels&step=publish",
            parcels.QuerySelector("[data-expose]")!.GetAttribute("href"));
        var draft = page.Find("tr[data-resource-node='draft']");
        Assert.Equal("Not served", draft.QuerySelector("[data-catalog-row-type]")!.TextContent);
        Assert.Equal("/console/operate/data/new?source=existingresource&resource=draft",
            draft.QuerySelector("[data-publish]")!.GetAttribute("href"));
        Assert.Equal("Results 1 to 2 (out of 2 layers)", page.Find("[data-catalog-results]").TextContent);
        Assert.Equal("parcels", Assert.Single(registry.Current.Selection).ResourceId);
        Assert.DoesNotContain("private_change", System.Text.Json.JsonSerializer.Serialize(registry.Current));
        Assert.Equal(0, registry.Current.Unsaved.PendingOperationCount);
        CatalogAdminPreview.Capture("data-datasets-focused", page.Markup);
    }

    [Fact]
    public void Datasets_V4FocusQueryAndMapActionSelectTheCanonicalResource()
    {
        var (context, registry) = CreateContext(new ConsoleV4Options { Enabled = true });
        using var ctx = context;
        var navigation = ctx.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/console/operate/data?focus=draft");
        var page = ctx.Render<OperateDataPage>();
        page.WaitForAssertion(() => Assert.Single(page.FindAll("[data-preview-empty]")));
        Assert.Contains("not published yet", page.Find("[data-preview-empty]").TextContent);
        Assert.Equal("draft", Assert.Single(registry.Current.Selection).ResourceId);

        page.Find("tr[data-resource-node='parcels'] [data-catalog-map]").Click();
        Assert.Equal("parcels", Assert.Single(registry.Current.Selection).ResourceId);
        Assert.Contains("parcels", page.Find("[data-layer-preview] h2").TextContent);
        CatalogAdminPreview.Capture("data-datasets", page.Markup);
    }

    [Fact]
    public void Datasets_V4AllFormatsSelectPreviewsThatProtocolAndShowsTheSelection()
    {
        var (context, _) = CreateContext(new ConsoleV4Options { Enabled = true });
        using var ctx = context;
        var page = ctx.Render<OperateDataPage>();
        page.WaitForAssertion(() => Assert.Single(page.FindAll("[data-layer-preview-catalog]")));
        var protocol = page.FindAll("tr[data-resource-node='parcels'] [data-catalog-all-formats] option")[^1];

        page.Find("tr[data-resource-node='parcels'] [data-catalog-all-formats]").Change(protocol.GetAttribute("value"));

        Assert.EndsWith(protocol.TextContent, page.Find("[data-layer-preview] h2").TextContent);
        Assert.Equal(protocol.GetAttribute("value"), page.Find("tr[data-resource-node='parcels'] [data-catalog-all-formats]").GetAttribute("value"));
    }

    [Fact]
    public void Datasets_V4TypeFilterAndSearchNarrowRowsAndAnnounceNoMatch()
    {
        var (context, _) = CreateContext(new ConsoleV4Options { Enabled = true });
        using var ctx = context;
        var page = ctx.Render<OperateDataPage>();
        page.WaitForAssertion(() => Assert.Single(page.FindAll("[data-layer-preview-catalog]")));

        page.Find("[data-catalog-type]").Change("Not served");
        Assert.Equal("draft", Assert.Single(page.FindAll("tbody tr")).GetAttribute("data-resource-node"));

        page.Find("[data-catalog-search]").Input(" no-such-layer");
        Assert.Equal(" no-such-layer", page.Find("[data-catalog-search]").GetAttribute("value"));
        Assert.Equal("No layers match this search.", page.Find("[role='status'][data-catalog-results]").TextContent);
        Assert.Empty(page.FindAll("tbody tr"));

        page.Find("[data-catalog-empty] button").Click();
        Assert.Equal(2, page.FindAll("tbody tr").Count);
        Assert.Equal("Results 1 to 2 (out of 2 layers)", page.Find("[data-catalog-results]").TextContent);
    }

    [Fact]
    public void Datasets_V4PagesLongInventoriesWithTheSharedPager()
    {
        var (context, _) = CreateContext(new ConsoleV4Options { Enabled = true }, new DatasetDataSource(extraDrafts: 28));
        using var ctx = context;
        var page = ctx.Render<OperateDataPage>();
        page.WaitForAssertion(() => Assert.Equal(25, page.FindAll("tbody tr").Count));
        Assert.Equal("Results 1 to 25 (out of 30 layers)", page.Find("[data-catalog-results]").TextContent);
        Assert.Single(page.FindAll("[data-layer-preview-catalog] .catalog-admin-paging"));
        CatalogAdminPreview.Capture("data-datasets-paged", page.Markup);
    }

    [Fact]
    public void LayerPreviewCatalog_PagesTwentyFiveRowsWithTheSharedPager()
    {
        using var ctx = CatalogContext();
        var catalog = ctx.Render<LayerPreviewCatalog>(parameters => parameters.Add(p => p.Rows, CatalogRows(30)));

        Assert.Equal(25, catalog.FindAll("tbody tr").Count);
        Assert.Equal("Results 1 to 25 (out of 30 layers)", catalog.Find("[data-catalog-results]").TextContent);
        Assert.True(catalog.Find(".catalog-admin-paging button").HasAttribute("disabled"));

        catalog.FindAll(".catalog-admin-paging button")[^1].Click();
        Assert.Equal(5, catalog.FindAll("tbody tr").Count);
        Assert.Equal("Results 26 to 30 (out of 30 layers)", catalog.Find("[data-catalog-results]").TextContent);

        catalog.Find("[data-catalog-type]").Change("Point");
        Assert.Equal(15, catalog.FindAll("tbody tr").Count);
        Assert.Empty(catalog.FindAll(".catalog-admin-paging"));
    }

    [Fact]
    public void LayerPreviewCatalog_RevealsTheSelectedLayerWhenTheSelectionChanges()
    {
        using var ctx = CatalogContext();
        var catalog = ctx.Render<LayerPreviewCatalog>(parameters => parameters.Add(p => p.Rows, CatalogRows(30)));
        catalog.FindAll(".catalog-admin-paging button")[^1].Click();
        Assert.Single(catalog.FindAll("tr[data-resource-node='layer-28']"));

        catalog.Render(parameters => parameters.Add(p => p.SelectedResourceId, "layer-02"));

        Assert.Equal("true", catalog.Find("tr[data-resource-node='layer-02']").GetAttribute("aria-current"));
        Assert.Equal("Results 1 to 25 (out of 30 layers)", catalog.Find("[data-catalog-results]").TextContent);
    }

    [Fact]
    public void LayerPreviewCatalog_MapActionPreviewsTheFirstServedRoute()
    {
        using var ctx = CatalogContext();
        LayerPreviewPane.PreviewSelection? selected = null;
        var catalog = ctx.Render<LayerPreviewCatalog>(parameters => parameters
            .Add(p => p.Rows, CatalogRows(3))
            .Add(p => p.OnPublicationSelected, (LayerPreviewPane.PreviewSelection selection) => selected = selection));

        catalog.Find("tr[data-resource-node='layer-02'] [data-catalog-map]").Click();

        Assert.Equal(new LayerPreviewPane.PreviewSelection("layer-02", "Layer 02", "ogc-features", "OGC API Features",
            "/ogc/features/collections/layer-02"), selected);
    }

    private static BunitContext CatalogContext()
    {
        var ctx = new BunitContext();
        ctx.Services.AddSingleton<NavigationManager>(new MountedNavigationManager("/console/", "operate/data"));
        return ctx;
    }

    private static LayerPreviewCatalog.Row[] CatalogRows(int count) => Enumerable.Range(1, count)
        .Select(index => new LayerPreviewCatalog.Row(
            new ResourceTreeNode($"layer-{index:00}", $"Layer {index:00}", "file", "Running", 0, null,
                [new PublicationTreeNode("ogc-features", "OGC API Features", true, $"/ogc/features/collections/layer-{index:00}")]),
            index % 2 == 0 ? "Point" : "Polygon"))
        .ToArray();

    private static (BunitContext Context, ConsoleAgentContextRegistry Registry) CreateContext(
        ConsoleV4Options options, DatasetDataSource? dataSource = null)
    {
        var ctx = new BunitContext();
        ctx.AddConsoleNotifications();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        var registry = new ConsoleAgentContextRegistry();
        ctx.Services.AddSingleton(registry);
        ctx.Services.AddSingleton(options);
        ctx.Services.AddSingleton<NavigationManager>(new MountedNavigationManager("/console/", "operate/data"));
        ctx.Services.AddSingleton<IOperateTransitionDataSource>(dataSource ?? new DatasetDataSource());
        return (ctx, registry);
    }

}
