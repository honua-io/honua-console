using Bunit;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class ScenesPageRenderTests
{
    [Fact]
    public void Scenes_WhenBound_RendersDiscoveryAndIngestForm()
    {
        using var ctx = new Bunit.BunitContext();
        ctx.Services.AddSingleton<IConsoleSceneClient>(new InMemoryConsoleSceneClient());

        var page = ctx.Render<OperateScenesPage>();

        page.WaitForAssertion(
            () => Assert.Contains("data-scenes-table", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        // LAS ingest flow (upload -> result) is present.
        Assert.Contains("data-pointcloud-ingest", page.Markup, StringComparison.Ordinal);
        Assert.Contains("data-pointcloud-file", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Demo point cloud", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Scenes_WhenSceneSelected_RendersTilesetViewer()
    {
        using var ctx = new Bunit.BunitContext();
        // The viewer attempts a JS module import for the live Cesium runtime; in the
        // render harness there is none, so loose mode lets the import no-op and the
        // component keeps its schematic placeholder (the no-mock contract).
        ctx.JSInterop.Mode = Bunit.JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IConsoleSceneClient>(new InMemoryConsoleSceneClient());

        var page = ctx.Render<OperateScenesPage>(parameters =>
            parameters.Add(p => p.SceneId, "demo-pointcloud"));

        page.WaitForAssertion(
            () => Assert.Contains("data-scene-viewer", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        // The resolved tileset.json serve route is surfaced to the viewer.
        Assert.Contains("/scenes/demo-pointcloud/tileset.json", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Scenes_MergedBuild_RendersMissingBindingThroughRealDi()
    {
        using var ctx = new Bunit.BunitContext();
        ctx.Services.AddSingleton<IConsoleSceneClient, UnsupportedConsoleSceneClient>();

        var page = ctx.Render<OperateScenesPage>();

        page.WaitForAssertion(
            () => Assert.Contains("Temporarily unavailable", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.DoesNotContain("data-scenes-table", page.Markup, StringComparison.Ordinal);
    }
}
