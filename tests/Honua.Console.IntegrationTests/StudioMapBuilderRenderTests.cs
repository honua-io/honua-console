using System.Net;
using System.Text;
using System.Text.Json;
using AngleSharp.Dom;
using Bunit;
using Honua.Console.Contracts;
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

        // Design tab (default) renders the three-column design surface: section tabs, the layer rail,
        // the live preview canvas, and the selected-layer inspector — matching the StudioMapEditor mockup.
        Assert.NotNull(page.Find("nav.console-tab-row"));
        Assert.NotNull(page.Find(".studio-map-design-grid"));
        Assert.NotNull(page.Find(".studio-map-layer-rail"));
        Assert.NotNull(page.Find(".studio-map-preview"));
        Assert.NotNull(page.Find(".studio-map-inspector"));
        Assert.Contains("Layer stack", page.Markup, StringComparison.Ordinal);
        Assert.False(FindButton(page, "Publish").HasAttribute("disabled"));

        // The Access tab surfaces the publish review (visibility options + dependencies), per the
        // StudioMapPublish mockup. Publish review is not on the default design tab.
        Assert.DoesNotContain("Publish review", page.Markup, StringComparison.Ordinal);
        FindButton(page, "Access").Click();
        page.WaitForAssertion(
            () => Assert.Contains("Publish review", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.NotNull(page.Find(".studio-map-visibility-options"));
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
            () => Assert.Contains("data-map-builder", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        // Publish stays gated on every tab while requirements are unmet.
        Assert.True(FindButton(page, "Publish").HasAttribute("disabled"));

        // The unmet pre-publish requirements list lives in the Access (publish-review) tab.
        FindButton(page, "Access").Click();
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

    [Fact]
    public void MapBuilder_ServerBound_NewMapSaveThenPublish_RendersLifecycleFromTypedClient()
    {
        // Drives the page through the production server-bound data source over a recording HttpClient, so
        // the binding (create draft -> freeze content version -> publication request) is exercised
        // end-to-end through the typed lifecycle client rather than a hand-written fake.
        var draftId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        var handler = new ServerFlowHandler(draftId, itemId, versionId);
        var baseUri = new Uri("https://server.example");
        var httpClient = new HttpClient(handler) { BaseAddress = baseUri };
        var client = new HttpStudioPackageLifecycleClient(
            httpClient,
            new StudioPackageLifecycleClientOptions(baseUri, "key"));

        using var ctx = new Bunit.TestContext();
        ctx.Services.AddSingleton<IStudioMapPackageDataSource>(new HonuaServerStudioMapPackageDataSource(client));

        var page = ctx.RenderComponent<StudioMapBuilderPage>();

        // The list view surfaces the no-list-verb capability state (never a fabricated package list).
        page.WaitForAssertion(
            () => Assert.Contains("Map packages cannot be listed yet", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        FindButton(page, "New map").Click();
        page.WaitForAssertion(
            () => Assert.Contains("data-map-builder", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        // Author a publish-ready map directly on the rendered editor, then save (creates the live draft).
        page.Find("input[placeholder='Public works map']").Change("Public works");
        page.Find("input[placeholder='basemap:streets']").Change("basemap:streets");
        page.Find("input[placeholder='-158.3,21.2,-157.6,21.7']").Change("-158.3,21.2,-157.6,21.7");
        FindButton(page, "Add layer").Click();
        page.Find("input[placeholder='content:hydrants@v12']").Change("content:hydrants@v12");

        FindButton(page, "Save draft").Click();
        page.WaitForAssertion(
            () => Assert.Contains("Saved map draft", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        FindButton(page, "Publish").Click();
        page.WaitForAssertion(
            () => Assert.Contains("Publication request accepted", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        // After publish the editor is terminal: publish is disabled and reopen is offered.
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

    /// <summary>
    /// Minimal honua-server stand-in that answers the create-draft, save-content-version, and
    /// publish-request routes the map publish flow drives, in the wire envelope the typed client expects.
    /// </summary>
    private sealed class ServerFlowHandler : HttpMessageHandler
    {
        private readonly Guid _draftId;
        private readonly Guid _itemId;
        private readonly Guid _versionId;

        public ServerFlowHandler(Guid draftId, Guid itemId, Guid versionId)
        {
            _draftId = draftId;
            _itemId = itemId;
            _versionId = versionId;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;

            object? data = path switch
            {
                "/api/v1/studio/package-drafts" => new
                {
                    draftId = _draftId,
                    itemId = Guid.Empty,
                    packageKey = "studio-map-public-works",
                    family = "map",
                    generation = 1,
                    envelope = new { family = "map", schemaVersion = StudioMapPackageMapper.SchemaVersion },
                    validation = new { status = "not-validated" },
                    createdAt = "2026-05-30T00:00:00Z",
                    updatedAt = "2026-05-30T00:00:00Z"
                },
                _ when path.EndsWith("/content-versions", StringComparison.Ordinal) => new
                {
                    itemId = _itemId,
                    packageKey = "studio-map-public-works",
                    versionId = _versionId,
                    versionNumber = 1,
                    contentHash = "abc",
                    envelope = new { family = "map", schemaVersion = StudioMapPackageMapper.SchemaVersion },
                    validation = new { status = "valid" },
                    createdAt = "2026-05-30T00:00:00Z"
                },
                _ when path.EndsWith("/publish-requests", StringComparison.Ordinal) => new
                {
                    requestId = Guid.NewGuid(),
                    itemId = _itemId,
                    versionId = _versionId,
                    status = "accepted",
                    validation = new { status = "valid" },
                    createdAt = "2026-05-30T00:00:00Z"
                },
                _ => null
            };

            if (data is null)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent(
                        """{"success":false,"message":"missing fixture"}""",
                        Encoding.UTF8,
                        "application/json")
                });
            }

            var json = JsonSerializer.Serialize(new { success = true, data });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }

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
