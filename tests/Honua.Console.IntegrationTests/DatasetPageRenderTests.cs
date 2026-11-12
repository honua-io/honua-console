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
        page.Find("[data-resource-node='parcels'] .resource-tree__name").Click();
        Assert.Equal(0, registry.Current.Unsaved.PendingOperationCount);
    }

    [Fact]
    public async Task Datasets_V4CatalogPreviewsSelectionAndQueryChangesUseCanonicalResource()
    {
        var (context, registry) = CreateContext(new ConsoleV4Options { Enabled = true });
        using var ctx = context;
        var navigation = ctx.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/console/operate/data?focus=parcels");
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
        Assert.Contains("Results 1 to 2 (out of 2 layers)", page.Find("[data-catalog-results]").TextContent);
        Assert.Equal("parcels", Assert.Single(registry.Current.Selection).ResourceId);
        CatalogAdminPreview.Capture("data-datasets-focused", page.Markup);

        await page.InvokeAsync(() => navigation.NavigateTo("/console/operate/data?focus=draft"));
        page.WaitForAssertion(() => Assert.Single(page.FindAll("[data-preview-empty]")));
        Assert.Equal("draft", Assert.Single(registry.Current.Selection).ResourceId);

        page.Find("tr[data-resource-node='parcels'] [data-catalog-map]").Click();
        Assert.Equal("parcels", Assert.Single(registry.Current.Selection).ResourceId);
        Assert.Contains("parcels", page.Find("[data-layer-preview] h2").TextContent);
        var protocol = page.FindAll("tr[data-resource-node='parcels'] [data-catalog-all-formats] option")[^1];
        page.Find("[data-catalog-search]").Input("draft");
        page.Find("tr[data-resource-node='draft'] [data-publish]");
        page.Find("[data-catalog-search]").Input(string.Empty);
        page.Find("tr[data-resource-node='parcels'] [data-catalog-all-formats]").Change(protocol.GetAttribute("value"));
        Assert.EndsWith(protocol.TextContent, page.Find("[data-layer-preview] h2").TextContent);

        page.Find("[data-catalog-type]").Change("Not served");
        Assert.Equal("draft", Assert.Single(page.FindAll("tbody tr")).GetAttribute("data-resource-node"));
        page.Find("[data-catalog-search]").Input("no-such-layer");
        Assert.Contains("No layers match this search.", page.Find("[data-tree-empty]").TextContent);
        page.Find("[data-tree-empty] button").Click();
        Assert.Equal(2, page.FindAll("tbody tr").Count);

        Assert.DoesNotContain("private_change", System.Text.Json.JsonSerializer.Serialize(registry.Current));
        Assert.Equal(0, registry.Current.Unsaved.PendingOperationCount);
        CatalogAdminPreview.Capture("data-datasets", page.Markup);
    }

    [Fact]
    public void LayerPreviewCatalog_PagesTwentyFiveRowsAndRevealsTheSelectedLayer()
    {
        using var ctx = new BunitContext();
        ctx.Services.AddSingleton<NavigationManager>(new MountedNavigationManager("/console/", "operate/data"));
        var rows = Enumerable.Range(1, 30)
            .Select(index => new LayerPreviewCatalog.Row(
                new ResourceTreeNode($"layer-{index:00}", $"Layer {index:00}", "file", "Running", 0, null,
                    [new PublicationTreeNode("ogc-features", "OGC API Features", true, $"/ogc/features/collections/layer-{index:00}")]),
                index % 2 == 0 ? "Point" : "Polygon"))
            .ToArray();
        LayerPreviewPane.PreviewSelection? selected = null;
        var catalog = ctx.Render<LayerPreviewCatalog>(parameters => parameters
            .Add(p => p.Rows, rows)
            .Add(p => p.SelectedResourceId, "layer-28")
            .Add(p => p.OnPublicationSelected, (LayerPreviewPane.PreviewSelection selection) => selected = selection));

        Assert.Equal(5, catalog.FindAll("tbody tr").Count);
        Assert.Contains("Results 26 to 30 (out of 30 layers)", catalog.Find("[data-catalog-results]").TextContent);
        Assert.Equal("true", catalog.Find("tr[data-resource-node='layer-28']").GetAttribute("aria-current"));
        Assert.Equal("Page 2", catalog.Find("[data-catalog-pager='top'] [aria-current='page']").GetAttribute("aria-label"));

        catalog.Find("[data-catalog-pager='top'] [aria-label='Previous page']").Click();
        Assert.Equal(25, catalog.FindAll("tbody tr").Count);
        Assert.True(catalog.Find("[data-catalog-pager='top'] [aria-label='Previous page']").HasAttribute("disabled"));
        Assert.Contains("Results 1 to 25 (out of 30 layers)", catalog.Find("[data-catalog-results]").TextContent);

        catalog.Find("[data-catalog-pager='bottom'] [aria-label='Next page']").Click();
        catalog.Find("[data-catalog-type]").Change("Point");
        Assert.Equal(15, catalog.FindAll("tbody tr").Count);
        Assert.Empty(catalog.FindAll("[data-catalog-pager='top'] button"));

        catalog.Find("tr[data-resource-node='layer-02'] [data-catalog-map]").Click();
        Assert.Equal("ogc-features", selected?.ProtocolId);
        Assert.Equal("/ogc/features/collections/layer-02", selected?.Route);
    }

    private static (BunitContext Context, ConsoleAgentContextRegistry Registry) CreateContext(ConsoleV4Options options)
    {
        var ctx = new BunitContext();
        ctx.AddConsoleNotifications();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        var registry = new ConsoleAgentContextRegistry();
        ctx.Services.AddSingleton(registry);
        ctx.Services.AddSingleton(options);
        ctx.Services.AddSingleton<NavigationManager>(new MountedNavigationManager("/console/", "operate/data"));
        ctx.Services.AddSingleton<IOperateTransitionDataSource>(new DatasetDataSource());
        return (ctx, registry);
    }

}
