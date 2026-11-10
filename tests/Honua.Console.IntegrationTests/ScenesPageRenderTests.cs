using Bunit;
using Honua.Console.Shell.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class ScenesPageRenderTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Presentation_UsesSharedFlagAndKeepsLinksInsideConsoleMount(bool enabled)
    {
        using var ctx = new BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IConsoleSceneClient>(new InMemoryConsoleSceneClient());
        ctx.Services.AddSingleton(new ConsoleV4Options { Enabled = enabled });
        ctx.Services.AddSingleton<NavigationManager>(new HostedNavigationManager());
        var page = ctx.Render<OperateScenesPage>();

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("a[href]")));
        Assert.Equal(enabled ? 1 : 0, page.FindAll(".console-page-heading-v4").Count);
        if (enabled)
        {
            Assert.Equal("Scenes", page.Find("h1").TextContent);
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
        ctx.Services.AddSingleton<IConsoleSceneClient>(new InMemoryConsoleSceneClient());
        ctx.Services.AddSingleton(new ConsoleV4Options { Enabled = true });
        var registry = new ConsoleAgentContextRegistry();
        ctx.Services.AddSingleton(registry);
        var page = ctx.Render<OperateScenesPage>(p => p.Add(x => x.SceneId, "demo-pointcloud"));

        page.WaitForAssertion(() => Assert.Equal("/operate/scenes/demo-pointcloud", registry.Current.Route));
        Assert.NotEmpty(registry.Current.Filters["sceneId"]);
        page.Render(p => p.Add(x => x.SceneId, null));
        Assert.Empty(registry.Current.Filters);
        page.Dispose();
        Assert.Empty(registry.Current.Filters);
        Assert.Empty(registry.Current.Selection);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Scenes_WhenBound_RendersDiscoveryAndIngestForm(bool enabled)
    {
        using var ctx = new Bunit.BunitContext();
        ctx.Services.AddSingleton(new ConsoleV4Options { Enabled = enabled });
        ctx.Services.AddSingleton<IConsoleSceneClient>(new InMemoryConsoleSceneClient());

        var page = ctx.Render<OperateScenesPage>();

        page.WaitForAssertion(
            () => Assert.Contains("data-scenes-table", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        // LAS ingest flow (upload -> result) is present.
        Assert.Contains("data-pointcloud-ingest", page.Markup, StringComparison.Ordinal);
        Assert.Contains("data-pointcloud-file", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Demo point cloud", page.Markup, StringComparison.Ordinal);
        Assert.Equal("Actions", page.Find("[data-scenes-table] th:last-child").GetAttribute("aria-label"));
        if (enabled) Assert.Equal("Actions", page.Find("[data-scenes-table] th:last-child").TextContent);
        if (enabled) CatalogAdminPreview.Capture("scenes-list", page.Markup);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Scenes_WhenSceneSelected_RendersTilesetViewer(bool enabled)
    {
        using var ctx = new Bunit.BunitContext();
        ctx.Services.AddSingleton(new ConsoleV4Options { Enabled = enabled });
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
        if (enabled) CatalogAdminPreview.Capture("scenes-detail", page.Markup);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Scenes_MergedBuild_RendersMissingBindingThroughRealDi(bool enabled)
    {
        using var ctx = new Bunit.BunitContext();
        ctx.Services.AddSingleton(new ConsoleV4Options { Enabled = enabled });
        ctx.Services.AddSingleton<IConsoleSceneClient, UnsupportedConsoleSceneClient>();

        var page = ctx.Render<OperateScenesPage>();

        page.WaitForAssertion(
            () => Assert.Contains("Temporarily unavailable", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.DoesNotContain("data-scenes-table", page.Markup, StringComparison.Ordinal);
        if (enabled) CatalogAdminPreview.Capture("scenes-missing", page.Markup);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Ingest_ClearsSubmittedDraftOnlyAfterSuccess(bool succeeds)
    {
        using var ctx = new BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IConsoleSceneClient>(new InMemoryConsoleSceneClient());
        ctx.Services.AddSingleton(new ConsoleV4Options { Enabled = true });
        var registry = new ConsoleAgentContextRegistry();
        ctx.Services.AddSingleton(registry);
        var page = ctx.Render<OperateScenesPage>();

        page.Find("[data-pointcloud-display-name]").Change("Survey");
        page.Find("[data-pointcloud-description]").Change("Uploaded survey");
        page.Find("[aria-label='Source CRS EPSG code']").Change("4326");
        page.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText(succeeds ? "LAS sample" : string.Empty, "survey.las"));
        page.WaitForAssertion(() => Assert.Equal(4, registry.Current.Unsaved.ChangedFieldCount));
        page.Find("[data-pointcloud-ingest-button]").Click();

        page.WaitForAssertion(() => Assert.Equal(succeeds ? 0 : 4, registry.Current.Unsaved.ChangedFieldCount));
        Assert.Equal(0, registry.Current.Unsaved.PendingOperationCount);
        Assert.Equal(succeeds ? string.Empty : "Survey", page.Find("[data-pointcloud-display-name]").GetAttribute("value"));
        Assert.Equal(succeeds, page.Find("[data-pointcloud-ingest-button]").HasAttribute("disabled"));
        if (succeeds) Assert.Contains("Survey", page.Find("[data-scenes-table]").TextContent);
    }

    [Fact]
    public async Task Ingest_DisablesDraftControlsUntilPendingRequestCompletes()
    {
        using var ctx = new BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        var client = new DelayedSceneClient();
        ctx.Services.AddSingleton<IConsoleSceneClient>(client);
        ctx.Services.AddSingleton(new ConsoleV4Options { Enabled = true });
        var page = ctx.Render<OperateScenesPage>();
        page.Find("[data-pointcloud-display-name]").Change("Survey");
        page.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText("LAS sample", "survey.las"));

        var pendingClick = page.Find("[data-pointcloud-ingest-button]").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());
        page.WaitForAssertion(() =>
        {
            Assert.True(client.Started);
            Assert.All(page.FindAll("[data-pointcloud-ingest] input"), input => Assert.True(input.HasAttribute("disabled")));
        });
        client.Complete();
        await pendingClick;

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("[data-pointcloud-ingest-success]")));
        Assert.All(page.FindAll("[data-pointcloud-ingest] input"), input => Assert.False(input.HasAttribute("disabled")));
        Assert.Equal(string.Empty, page.Find("[data-pointcloud-display-name]").GetAttribute("value"));
    }

    private sealed class DelayedSceneClient : IConsoleSceneClient
    {
        private readonly InMemoryConsoleSceneClient _inner = new();
        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Started { get; private set; }
        public void Complete() => _completion.SetResult();
        public Task<SceneReadResult<SceneListResponse>> ListScenesAsync(CancellationToken cancellationToken = default)
            => _inner.ListScenesAsync(cancellationToken);
        public Task<string?> ResolveTilesetUrlAsync(string sceneId, CancellationToken cancellationToken = default)
            => _inner.ResolveTilesetUrlAsync(sceneId, cancellationToken);
        public async Task<SceneReadResult<PointCloudIngestResult>> IngestPointCloudAsync(
            PointCloudIngestRequest request, CancellationToken cancellationToken = default)
        {
            Started = true;
            await _completion.Task.WaitAsync(cancellationToken);
            return await _inner.IngestPointCloudAsync(request, cancellationToken);
        }
    }

    private sealed class HostedNavigationManager : NavigationManager
    {
        public HostedNavigationManager() => Initialize("http://localhost/console/", "http://localhost/console/operate");
    }
}
