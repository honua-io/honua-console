using Bunit;
using Honua.Console.Shell.Models;
using Microsoft.AspNetCore.Components;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class SensorThingsPageRenderTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Presentation_UsesSharedFlagAndKeepsLinksInsideConsoleMount(bool enabled)
    {
        using var ctx = new BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IConsoleSensorThingsClient>(new InMemoryConsoleSensorThingsClient());
        ctx.Services.AddSingleton(new ConsoleV4Options { Enabled = enabled });
        ctx.Services.AddSingleton<NavigationManager>(new MountedNavigationManager("/console/", "operate"));
        var page = ctx.Render<OperateSensorThingsPage>();

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("a[href]")));
        Assert.Equal(enabled ? 1 : 0, page.FindAll(".console-page-heading-v4").Count);
        if (enabled)
        {
            Assert.Equal("SensorThings", page.Find("h1").TextContent);
            Assert.NotEmpty(page.FindAll(".console-panel[aria-labelledby]"));
        }
        foreach (var link in page.FindAll("a[href]"))
        {
            Assert.StartsWith("/console/operate/", link.GetAttribute("href"));
        }
    }

    [Fact]
    public void DetailContext_TracksRouteChangesAndClearsOnDispose()
    {
        using var ctx = new BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IConsoleSensorThingsClient>(new InMemoryConsoleSensorThingsClient());
        ctx.Services.AddSingleton(new ConsoleV4Options { Enabled = true });
        var registry = new ConsoleAgentContextRegistry();
        ctx.Services.AddSingleton(registry);
        var page = ctx.Render<OperateSensorThingsPage>(p => p.Add(x => x.DatastreamId, 1L));

        page.WaitForAssertion(() => Assert.Equal("/operate/sensors/datastreams/1", registry.Current.Route));
        Assert.NotEmpty(registry.Current.Filters["datastreamId"]);
        page.Render(p => p.Add(x => x.DatastreamId, null));
        Assert.Empty(registry.Current.Filters);
        page.Dispose();
        Assert.Empty(registry.Current.Filters);
        Assert.Empty(registry.Current.Selection);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Browser_WhenBound_RendersThingsAndDatastreams(bool enabled)
    {
        using var ctx = new Bunit.BunitContext();
        ctx.Services.AddSingleton(new ConsoleV4Options { Enabled = enabled });
        ctx.Services.AddSingleton<IConsoleSensorThingsClient>(new InMemoryConsoleSensorThingsClient());

        var page = ctx.Render<OperateSensorThingsPage>();

        page.WaitForAssertion(
            () => Assert.Contains("data-sensorthings-datastreams-table", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Contains("Air temperature", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Demo weather station", page.Markup, StringComparison.Ordinal);
        if (enabled) CatalogAdminPreview.Capture("sensors-list", page.Markup);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Browser_WhenDatastreamSelected_RendersTimeSeriesChart(bool enabled)
    {
        using var ctx = new Bunit.BunitContext();
        ctx.Services.AddSingleton(new ConsoleV4Options { Enabled = enabled });
        ctx.Services.AddSingleton<IConsoleSensorThingsClient>(new InMemoryConsoleSensorThingsClient());

        // Synthetic seeded datastream id=1 from honua-server #1842.
        var page = ctx.Render<OperateSensorThingsPage>(parameters =>
            parameters.Add(p => p.DatastreamId, 1L));

        page.WaitForAssertion(
            () => Assert.Contains("data-timeseries-line", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Contains("data-sensorthings-detail", page.Markup, StringComparison.Ordinal);
        Assert.Contains("data-sensorthings-observations-table", page.Markup, StringComparison.Ordinal);
        Assert.Equal(enabled, page.Find("details summary").ClassList.Contains("console-button"));
        if (enabled) CatalogAdminPreview.Capture("sensors-detail", page.Markup);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Browser_MergedBuild_RendersMissingBindingThroughRealDi(bool enabled)
    {
        using var ctx = new Bunit.BunitContext();
        ctx.Services.AddSingleton(new ConsoleV4Options { Enabled = enabled });
        ctx.Services.AddSingleton<IConsoleSensorThingsClient, UnsupportedConsoleSensorThingsClient>();

        var page = ctx.Render<OperateSensorThingsPage>();

        page.WaitForAssertion(
            () => Assert.Contains("Temporarily unavailable", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        // No fabricated rows in the missing-binding state.
        Assert.DoesNotContain("data-sensorthings-datastreams-table", page.Markup, StringComparison.Ordinal);
        if (enabled) CatalogAdminPreview.Capture("sensors-missing", page.Markup);
    }
    [Fact]
    public void Metrics_UseServerTotalsWhenOnlyOnePageIsLoaded()
    {
        using var ctx = new BunitContext();
        var seed = new SensorThingsSeed(
            Enumerable.Range(1, 120).Select(id => new StaThing { IotId = id, Name = $"Thing {id}" }).ToArray(),
            Enumerable.Range(1, 75).Select(id => new StaDatastream { IotId = id, Name = $"Stream {id}" }).ToArray(),
            new Dictionary<long, IReadOnlyList<StaObservation>>());
        ctx.Services.AddSingleton<IConsoleSensorThingsClient>(new InMemoryConsoleSensorThingsClient(seed));
        ctx.Services.AddSingleton(new ConsoleV4Options { Enabled = true });
        var page = ctx.Render<OperateSensorThingsPage>();

        page.WaitForAssertion(() => Assert.Equal(50, page.FindAll("[data-sensorthings-things-table] tbody tr").Count));
        Assert.Contains("120", page.Find("[data-sensorthings-things] .console-metric-tile").TextContent);
        Assert.Contains("75", page.Find("[data-sensorthings-datastreams] .console-metric-tile").TextContent);
    }

}
