using Bunit;
using Honua.Console.Shell.Components.Operate;
using Honua.Console.Shell.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class LayerPreviewListV4Tests
{
    private static readonly OperateServiceDetail Emergency =
        new("emergency", "Emergency management", "FeatureServer", "Configured", "honua-server", [], [], []) { FolderId = "f1", FolderName = "public-safety" };

    private static readonly IReadOnlyList<(OperateServiceDetail Service, OperateServiceLayer Layer)> Rows =
        Enumerable.Range(1, 28).Select(i => (Emergency, new OperateServiceLayer(i, $"layer-{i:00}", i % 2 == 0 ? "Point" : "MultiPolygon", $"conn-1-layer-{i}", $"Dataset {i}")))
            .ToArray();

    [Fact]
    public void ListsPathsMapAndExportLinksWithAPager()
    {
        using var ctx = Context();
        var list = ctx.Render<LayerPreviewListV4>(p => p.Add(c => c.Rows, Rows));

        var rows = list.FindAll("tbody tr");
        Assert.Equal(25, rows.Count);
        Assert.Equal("public-safety/emergency/layer-01", rows[0].QuerySelector(".console-service-layer-path")!.TextContent);
        Assert.Equal("polygon", rows[0].QuerySelector("[data-icon]")!.GetAttribute("data-icon"));
        Assert.Equal("/console/operate/layers/conn-1-layer-1", rows[0].QuerySelector("a[aria-label='Map of layer-01']")!.GetAttribute("href"));
        Assert.Equal("/console/operate/exports/new?serviceId=emergency&layerId=1&name=layer-01",
            rows[0].QuerySelector("a[aria-label='Export layer-01']")!.GetAttribute("href"));

        list.Find("[aria-label='Next page']").Click();
        Assert.Equal(3, list.FindAll("tbody tr").Count);
    }

    [Fact]
    public void TypeFilterAndSearchNarrowTheListAndResetThePage()
    {
        using var ctx = Context();
        var list = ctx.Render<LayerPreviewListV4>(p => p.Add(c => c.Rows, Rows));
        list.Find("[aria-label='Next page']").Click();

        list.Find("[data-layer-type-filter]").Change("point");
        Assert.Equal(14, list.FindAll("tbody tr").Count);
        Assert.All(list.FindAll("tbody [data-icon]"), icon => Assert.Equal("point", icon.GetAttribute("data-icon")));

        list.Find("[data-layer-search]").Input("layer-02");
        Assert.Single(list.FindAll("tbody tr"));

        list.Find("[data-layer-search]").Input("no-such-layer");
        Assert.Equal("No layers match this search.", list.Find("[data-layer-list-empty]").TextContent);
    }

    private static BunitContext Context()
    {
        var ctx = new BunitContext();
        ctx.Services.AddSingleton<Microsoft.AspNetCore.Components.NavigationManager>(new MountedNavigationManager("/console/"));
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        return ctx;
    }
}
