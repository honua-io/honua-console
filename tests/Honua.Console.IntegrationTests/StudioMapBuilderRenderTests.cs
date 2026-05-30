using AngleSharp.Dom;
using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free render coverage for the map builder page (<c>/studio/map</c>). Verifies the missing-binding
/// surface, that the editor renders the authored layers and the publish-review surface (AC#2), and that
/// Publish stays gated until the pre-publish requirements are met. Drives the page through a fake
/// <see cref="IStudioMapPackageDataSource"/> rather than a mock server, matching the form-builder pattern.
/// </summary>
public sealed class StudioMapBuilderRenderTests
{
    [Fact]
    public void MapBuilder_WhenBindingMissing_RendersNotBoundSurface()
    {
        var data = new FakeMapDataSource
        {
            Workspace = new StudioMapWorkspace([], [MissingBinding])
        };
        using var ctx = new Bunit.TestContext();
        ctx.Services.AddSingleton<IStudioMapPackageDataSource>(data);

        var page = ctx.RenderComponent<StudioMapBuilderPage>();

        page.WaitForAssertion(
            () => Assert.Contains("Map package lifecycle is not bound", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.DoesNotContain("data-map-builder", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void MapBuilder_OpenReadyMap_RendersLayersAndPublishReviewAndEnablesPublish()
    {
        var data = new FakeMapDataSource
        {
            Workspace = new StudioMapWorkspace(
                [new StudioMapPackageListItem("map-1", "Public works", 1, 3, null, DateTimeOffset.UtcNow)],
                []),
            EditorLoad = new StudioMapEditorLoad(ReadyEditor(), [])
        };
        using var ctx = new Bunit.TestContext();
        ctx.Services.AddSingleton<IStudioMapPackageDataSource>(data);

        var page = ctx.RenderComponent<StudioMapBuilderPage>();
        page.WaitForAssertion(() => FindButton(page, "Public works"), TimeSpan.FromSeconds(5));
        FindButton(page, "Public works").Click();

        page.WaitForAssertion(
            () => Assert.Contains("data-map-builder", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Contains("Publish review", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Layer stack", page.Markup, StringComparison.Ordinal);
        Assert.False(FindButton(page, "Publish").HasAttribute("disabled"));
    }

    [Fact]
    public void MapBuilder_OpenIncompleteMap_GatesPublishWithUnmetRequirements()
    {
        var incomplete = new StudioMapEditorState { MapId = "map-2", Title = "Incomplete" };
        var data = new FakeMapDataSource
        {
            Workspace = new StudioMapWorkspace(
                [new StudioMapPackageListItem("map-2", "Incomplete", 0, 1, null, DateTimeOffset.UtcNow)],
                []),
            EditorLoad = new StudioMapEditorLoad(incomplete, [])
        };
        using var ctx = new Bunit.TestContext();
        ctx.Services.AddSingleton<IStudioMapPackageDataSource>(data);

        var page = ctx.RenderComponent<StudioMapBuilderPage>();
        page.WaitForAssertion(() => FindButton(page, "Incomplete"), TimeSpan.FromSeconds(5));
        FindButton(page, "Incomplete").Click();

        page.WaitForAssertion(
            () => Assert.Contains("Add at least one layer.", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.True(FindButton(page, "Publish").HasAttribute("disabled"));
    }

    [Fact]
    public void MapBuilder_OpenPublishedMap_DisablesPublishAndOffersReopen()
    {
        var data = new FakeMapDataSource
        {
            Workspace = new StudioMapWorkspace(
                [new StudioMapPackageListItem("map-1", "Public works", 1, null, 4, DateTimeOffset.UtcNow)],
                []),
            EditorLoad = new StudioMapEditorLoad(PublishedEditor(), [])
        };
        using var ctx = new Bunit.TestContext();
        ctx.Services.AddSingleton<IStudioMapPackageDataSource>(data);

        var page = ctx.RenderComponent<StudioMapBuilderPage>();
        page.WaitForAssertion(() => FindButton(page, "Public works"), TimeSpan.FromSeconds(5));
        FindButton(page, "Public works").Click();

        page.WaitForAssertion(
            () => Assert.Contains("data-map-builder", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        Assert.True(FindButton(page, "Publish").HasAttribute("disabled"));
        Assert.NotNull(FindButton(page, "Reopen as draft"));
    }

    private static IElement FindButton(IRenderedComponent<StudioMapBuilderPage> page, string label) =>
        page.FindAll("button").First(button => button.TextContent.Contains(label, StringComparison.Ordinal));

    private static StudioMapEditorState ReadyEditor()
    {
        var state = new StudioMapEditorState
        {
            MapId = "map-1",
            Version = 3,
            Title = "Public works",
            Basemap = "basemap:streets",
            InitialExtent = "-158.3,21.2,-157.6,21.7",
            ETag = "etag-3"
        };
        state.Layers.Add(new StudioMapLayerEditor { SourceRef = "content:hydrants@v12", Title = "Hydrants" });
        return state;
    }

    private static StudioMapEditorState PublishedEditor()
    {
        var state = ReadyEditor();
        state.Status = StudioMapStatuses.Published;
        return state;
    }

    private static readonly StudioMapCapabilityState MissingBinding = new(
        "Map builder",
        "Missing binding",
        "Honua:Server:BaseUrl",
        "Configure Honua:Server:BaseUrl so the map builder can bind the server-owned map package lifecycle.");

    private sealed class FakeMapDataSource : IStudioMapPackageDataSource
    {
        public StudioMapWorkspace Workspace { get; set; } = new([], []);

        public StudioMapEditorLoad EditorLoad { get; set; } = new(null, []);

        public Task<StudioMapWorkspace> GetWorkspaceAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Workspace);

        public Task<StudioMapEditorLoad> LoadAsync(string? mapId, CancellationToken cancellationToken = default) =>
            Task.FromResult(EditorLoad);

        public Task<StudioMapCommandResult> SaveDraftAsync(StudioMapEditorState state, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StudioMapCommandResult(true, "Saved.", state));

        public Task<StudioMapCommandResult> PublishAsync(StudioMapEditorState state, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StudioMapCommandResult(true, "Published.", state));

        public Task<StudioMapCommandResult> ReopenAsync(StudioMapEditorState state, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StudioMapCommandResult(true, "Reopened.", new StudioMapEditorState { MapId = state.MapId, Version = state.Version + 1 }));
    }
}
