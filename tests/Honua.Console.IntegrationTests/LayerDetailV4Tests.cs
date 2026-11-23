using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class LayerDetailV4Tests
{
    private static readonly OperateServiceLayer FloodZones =
        new(2, "flood-zones", "Polygon", "conn-1-layer-2", "Flood hazard areas", [-123.414, 46.72, -122.861, 47.108])
        {
            Srid = 4326,
            ConnectionId = "conn-1",
            ConnectionName = "county-postgis",
            Schema = "honua_data",
            Table = "flood_zones",
            Description = "FEMA flood hazard zones."
        };

    private static readonly OperateServiceDetail Emergency =
        new("emergency", "Emergency management", "FeatureServer", "Configured", "Owned by honua-server.", [FloodZones], [],
            [
                new("FeatureServer", "https://server.example.test/rest/services/public-safety/emergency/FeatureServer", "Exposed"),
                new("OGC API Features", null, "Collections only")
            ])
        { FolderId = "folder-1", FolderName = "public-safety" };

    [Fact]
    public void Header_SummarisesPathGeometrySrsAndSourceTable()
    {
        using var ctx = CreateContext();
        var page = Render(ctx);

        var summary = page.Find("[data-layer-summary]");
        Assert.Equal("Layer public-safety/emergency/flood-zones · Polygon · EPSG:4326 · table honua_data.flood_zones on county-postgis",
            System.Text.RegularExpressions.Regex.Replace(summary.TextContent, @"\s+", " ").Trim());
        Assert.Equal("/console/operate/connections/conn-1", summary.QuerySelector("a")!.GetAttribute("href"));
        Assert.Equal("FEMA flood hazard zones.", page.Find(".console-service-description").TextContent);
        Assert.Empty(page.FindAll(".console-page-heading-actions"));
    }

    [Fact]
    public void WebServices_ListServiceUrlsWithCopyAndOpen()
    {
        using var ctx = CreateContext();
        var page = Render(ctx);

        var rows = page.FindAll("[data-layer-endpoint]");
        Assert.Equal(2, rows.Count);
        Assert.Equal(Emergency.Endpoints[0].Url, rows[0].QuerySelector("a")!.GetAttribute("href"));
        Assert.NotNull(rows[0].QuerySelector("[data-console-copy]"));
        Assert.Null(rows[1].QuerySelector("a"));
        Assert.Contains("Collections only", rows[1].TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Administration_HoldsActionsServiceDatasetAndExtent()
    {
        using var ctx = CreateContext();
        var page = Render(ctx);

        var administration = page.Find(".console-service-administration");
        Assert.NotNull(administration.QuerySelector("[data-layer-style-link]"));
        Assert.NotNull(administration.QuerySelector("a[href='/console/operate/services/emergency/settings']"));
        Assert.NotNull(administration.QuerySelector("a[href='/console/operate/resources/conn-1-layer-2']"));
        var values = page.FindAll("[data-layer-administration] dd").Select(value => value.TextContent.Trim()).ToArray();
        Assert.Equal("Flood hazard areas on county-postgis", values[1]);
        Assert.Equal("-123.41, 46.72 → -122.86, 47.11", values[2]);
        Assert.Equal("Enabled", values[3]);
    }

    [Fact]
    public void LayerServedByTwoServicesNamesEachOne()
    {
        var planning = new OperateServiceDetail("planning", "Planning", "FeatureServer", "Configured", "Owned by honua-server.", [FloodZones], [],
            [new("FeatureServer", "https://server.example.test/rest/services/planning/FeatureServer", "Exposed")]);
        using var ctx = CreateContext(Emergency, planning);
        var page = Render(ctx);

        var administration = page.Find("[data-layer-administration]");
        Assert.Equal("Services", administration.QuerySelector("dt")!.TextContent);
        Assert.Equal(new[] { "Emergency management", "Planning" },
            administration.QuerySelector("dd")!.QuerySelectorAll("a").Select(link => link.TextContent).ToArray());
        var rows = page.FindAll("[data-layer-endpoint]");
        Assert.Equal(3, rows.Count);
        Assert.Equal("planning", rows[2].QuerySelector("small")!.TextContent);
        Assert.Equal("Open FeatureServer URL for planning (new tab)", rows[2].QuerySelector("a")!.GetAttribute("aria-label"));
    }

    private static IRenderedComponent<OperateLayerDetailPage> Render(BunitContext ctx)
    {
        var page = ctx.Render<OperateLayerDetailPage>(p => p.Add(c => c.ResourceId, FloodZones.CanonicalResourceId));
        page.WaitForAssertion(() => Assert.NotNull(page.Find("[data-layer-summary]")));
        return page;
    }

    private static BunitContext CreateContext(params OperateServiceDetail[] services)
    {
        var ctx = new BunitContext();
        ctx.Services.AddSingleton<Microsoft.AspNetCore.Components.NavigationManager>(new MountedNavigationManager("/console/"));
        ctx.Services.AddSingleton(new ConsoleV4Options { Enabled = true });
        ctx.Services.AddSingleton<ConsoleAgentContextRegistry>();
        ctx.AddConsoleNotifications();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IOperateTransitionDataSource>(new FixedServicesDataSource(services.Length == 0 ? new[] { Emergency } : services));
        ctx.Services.AddSingleton<IConsoleLayerFieldsOperation>(new UnsupportedConsoleLayerFieldsOperation());
        return ctx;
    }
}
