using Bunit;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free render coverage for the SensorThings browser
/// (/operate/sensors). Asserts the live browse + chart surface (driven by the
/// InMemory STA client) and the merged-build missing-binding surface (driven by
/// the real UnsupportedConsoleSensorThingsClient through DI). Drives the page
/// through fakes, never a mock server.
/// </summary>
public sealed class SensorThingsPageRenderTests
{
    [Fact]
    public void Browser_WhenBound_RendersThingsAndDatastreams()
    {
        using var ctx = new Bunit.BunitContext();
        ctx.Services.AddSingleton<IConsoleSensorThingsClient>(new InMemoryConsoleSensorThingsClient());

        var page = ctx.Render<OperateSensorThingsPage>();

        page.WaitForAssertion(
            () => Assert.Contains("data-sensorthings-datastreams-table", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Contains("Air temperature", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Demo weather station", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Browser_WhenDatastreamSelected_RendersTimeSeriesChart()
    {
        using var ctx = new Bunit.BunitContext();
        ctx.Services.AddSingleton<IConsoleSensorThingsClient>(new InMemoryConsoleSensorThingsClient());

        // Synthetic seeded datastream id=1 from honua-server #1842.
        var page = ctx.Render<OperateSensorThingsPage>(parameters =>
            parameters.Add(p => p.DatastreamId, 1L));

        page.WaitForAssertion(
            () => Assert.Contains("data-timeseries-line", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Contains("data-sensorthings-detail", page.Markup, StringComparison.Ordinal);
        Assert.Contains("data-sensorthings-observations-table", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Browser_MergedBuild_RendersMissingBindingThroughRealDi()
    {
        using var ctx = new Bunit.BunitContext();
        ctx.Services.AddSingleton<IConsoleSensorThingsClient, UnsupportedConsoleSensorThingsClient>();

        var page = ctx.Render<OperateSensorThingsPage>();

        page.WaitForAssertion(
            () => Assert.Contains("Temporarily unavailable", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        // No fabricated rows in the missing-binding state.
        Assert.DoesNotContain("data-sensorthings-datastreams-table", page.Markup, StringComparison.Ordinal);
    }
}
