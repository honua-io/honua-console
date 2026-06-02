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
        ctx.JSInterop.Mode = Bunit.JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IStudioMapPackageDataSource>(data);
        ctx.Services.AddSingleton<IStudioMapStyleCatalogDataSource, UnsupportedStudioMapStyleCatalogDataSource>();

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
        ctx.JSInterop.Mode = Bunit.JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IStudioMapPackageDataSource>(data);
        ctx.Services.AddSingleton<IStudioMapStyleCatalogDataSource, UnsupportedStudioMapStyleCatalogDataSource>();

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

        // The Access tab surfaces the multi-step publish wizard (Validate · Dependencies · Visibility ·
        // Embed · Rollback · Confirm), per the StudioMapPublish mockup. It is not on the default design tab.
        Assert.DoesNotContain("Publish review", page.Markup, StringComparison.Ordinal);
        FindButton(page, "Access").Click();
        page.WaitForAssertion(
            () => Assert.Contains("Publish review", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.NotNull(page.Find("[data-publish-wizard]"));
        // The wizard renders the full six-step rail.
        foreach (var label in new[] { "Validate", "Dependencies", "Visibility", "Embed", "Rollback", "Confirm" })
        {
            Assert.Contains(label, page.Markup, StringComparison.Ordinal);
        }

        // Advance Validate → Dependencies → Visibility; the visibility options live on the Visibility step.
        FindButton(page, "Continue · Dependencies").Click();
        page.WaitForAssertion(
            () => Assert.NotNull(page.Find("[data-wizard-step='dependencies']")),
            TimeSpan.FromSeconds(5));
        FindButton(page, "Continue · Visibility").Click();
        page.WaitForAssertion(
            () => Assert.NotNull(page.Find(".studio-map-visibility-options")),
            TimeSpan.FromSeconds(5));
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
        ctx.JSInterop.Mode = Bunit.JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IStudioMapPackageDataSource>(data);
        ctx.Services.AddSingleton<IStudioMapStyleCatalogDataSource, UnsupportedStudioMapStyleCatalogDataSource>();

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
        ctx.JSInterop.Mode = Bunit.JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IStudioMapPackageDataSource>(data);
        ctx.Services.AddSingleton<IStudioMapStyleCatalogDataSource, UnsupportedStudioMapStyleCatalogDataSource>();

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
        ctx.JSInterop.Mode = Bunit.JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IStudioMapPackageDataSource>(new HonuaServerStudioMapPackageDataSource(client));
        ctx.Services.AddSingleton<IStudioMapStyleCatalogDataSource, UnsupportedStudioMapStyleCatalogDataSource>();

        var page = ctx.RenderComponent<StudioMapBuilderPage>();

        // The list view surfaces the no-list-verb capability state (never a fabricated package list).
        page.WaitForAssertion(
            () => Assert.Contains("Map packages cannot be listed yet", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        // "New from prompt" opens the StudioMapAI conversation pane; "Open editor →" drops into the editor.
        FindButton(page, "New from prompt").Click();
        page.WaitForAssertion(
            () => Assert.Contains("data-map-ai", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        FindButton(page, "Open editor").Click();
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

        // "Publish…" opens the multi-step publish wizard on the Access tab; walk it to Confirm and finish.
        FindButton(page, "Publish…").Click();
        page.WaitForAssertion(
            () => Assert.NotNull(page.Find("[data-publish-wizard]")),
            TimeSpan.FromSeconds(5));
        foreach (var continueLabel in new[]
                 {
                     "Continue · Dependencies",
                     "Continue · Visibility",
                     "Continue · Embed",
                     "Continue · Rollback",
                     "Continue · Confirm",
                 })
        {
            FindButton(page, continueLabel).Click();
        }
        page.WaitForAssertion(
            () => Assert.NotNull(page.Find("[data-wizard-step='confirm']")),
            TimeSpan.FromSeconds(5));
        page.FindAll("button.publish-wizard-finish").Single().Click();
        page.WaitForAssertion(
            () => Assert.Contains("Publication request accepted", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        // After publish the editor is terminal: publish is disabled and reopen is offered.
        Assert.True(FindButton(page, "Publish…").HasAttribute("disabled"));
        Assert.NotNull(FindButton(page, "Reopen as draft"));
    }

    [Fact]
    public void MapBuilder_NewFromPrompt_RendersStudioMapAiConversationAndPackagePreview()
    {
        // "New from prompt" opens the StudioMapAI flow: a conversation pane on the left and a package
        // inspector + MapPreview (with a preview/mobile device toggle) on the right.
        var data = new FakeMapDataSource
        {
            Workspace = new StudioMapWorkspace([], []),
            EditorLoad = new StudioMapEditorLoad(new StudioMapEditorState(), [])
        };
        using var ctx = new Bunit.TestContext();
        ctx.JSInterop.Mode = Bunit.JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IStudioMapPackageDataSource>(data);
        ctx.Services.AddSingleton<IStudioMapStyleCatalogDataSource, UnsupportedStudioMapStyleCatalogDataSource>();

        var page = ctx.RenderComponent<StudioMapBuilderPage>();
        page.WaitForAssertion(() => FindButton(page, "New from prompt"), TimeSpan.FromSeconds(5));
        FindButton(page, "New from prompt").Click();

        page.WaitForAssertion(
            () => Assert.Contains("data-map-ai", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        // Conversation pane (shared StudioAiConversation) with the refine + send affordances.
        Assert.NotNull(page.Find("[data-studio-ai-pane]"));
        Assert.NotNull(page.Find(".studio-ai-conversation-log"));
        Assert.NotNull(page.Find(".studio-ai-refine-input"));

        // Right-side package inspector driven by the real authoring state, plus the shared MapPreview.
        Assert.NotNull(page.Find(".studio-map-ai-package"));
        Assert.NotNull(page.Find(".map-preview"));

        // Preview / Mobile device toggle.
        var deviceButtons = page.FindAll(".studio-map-ai-device button");
        Assert.Equal(2, deviceButtons.Count);
        Assert.Contains("Mobile", page.Markup, StringComparison.Ordinal);
        FindButton(page, "Mobile").Click();
        page.WaitForAssertion(
            () => Assert.NotNull(page.Find(".studio-map-ai-canvas-mobile")),
            TimeSpan.FromSeconds(5));

        // "view evidence" link surfaces after the author sends a prompt; the conversation never fabricates
        // package state (the inspector still reflects the empty editor — no layers bound).
        Assert.DoesNotContain("view evidence", page.Markup, StringComparison.Ordinal);
        page.Find(".studio-ai-refine-input").Input("Show parcels coloured by use code");
        FindButton(page, "Send").Click();
        page.WaitForAssertion(
            () => Assert.Contains("view evidence", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Contains("Show parcels coloured by use code", page.Markup, StringComparison.Ordinal);

        // "Open editor →" drops into the full editor; "Back to chat" returns to the conversation.
        FindButton(page, "Open editor").Click();
        page.WaitForAssertion(
            () => Assert.Contains("data-map-builder", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        FindButton(page, "Back to chat").Click();
        page.WaitForAssertion(
            () => Assert.Contains("data-map-ai", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void MapBuilder_AccessTab_RendersSixStepPublishWizardAndGatesOnValidate()
    {
        // The Access tab hosts the StudioMapPublish multi-step wizard. On an incomplete map the Validate
        // step gates forward navigation until the pre-publish requirements are met.
        var incomplete = new StudioMapEditorState { MapId = "map-2", Title = "Incomplete" };
        var data = new FakeMapDataSource
        {
            Workspace = new StudioMapWorkspace(
                [new StudioMapPackageListItem("map-2", "Incomplete", 0, 1, null, DateTimeOffset.UtcNow)],
                []),
            EditorLoad = new StudioMapEditorLoad(incomplete, [])
        };
        using var ctx = new Bunit.TestContext();
        ctx.JSInterop.Mode = Bunit.JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IStudioMapPackageDataSource>(data);
        ctx.Services.AddSingleton<IStudioMapStyleCatalogDataSource, UnsupportedStudioMapStyleCatalogDataSource>();

        var page = ctx.RenderComponent<StudioMapBuilderPage>();
        page.WaitForAssertion(() => FindButton(page, "Incomplete"), TimeSpan.FromSeconds(5));
        FindButton(page, "Incomplete").Click();
        page.WaitForAssertion(
            () => Assert.Contains("data-map-builder", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        FindButton(page, "Access").Click();
        page.WaitForAssertion(
            () => Assert.NotNull(page.Find("[data-publish-wizard]")),
            TimeSpan.FromSeconds(5));

        // All six labelled steps render in the stepper.
        foreach (var label in new[] { "Validate", "Dependencies", "Visibility", "Embed", "Rollback", "Confirm" })
        {
            Assert.Contains(label, page.Markup, StringComparison.Ordinal);
        }

        // The Validate step lists the unmet requirements and disables the forward control.
        Assert.NotNull(page.Find("[data-wizard-step='validate']"));
        Assert.Contains("Add at least one layer.", page.Markup, StringComparison.Ordinal);
        Assert.True(FindButton(page, "Continue · Dependencies").HasAttribute("disabled"));
    }

    [Fact]
    public void MapBuilder_AccessTab_ReadyMap_WizardWalksToConfirmAndPublishes()
    {
        var published = false;
        var data = new FakeMapDataSource
        {
            Workspace = new StudioMapWorkspace(
                [new StudioMapPackageListItem("map-1", "Public works", 1, 3, null, DateTimeOffset.UtcNow)],
                []),
            EditorLoad = new StudioMapEditorLoad(ReadyEditor(), []),
            OnPublish = state =>
            {
                published = true;
                state.Status = StudioMapStatuses.Published;
                return new StudioMapCommandResult(true, "Publication request accepted.", state);
            }
        };
        using var ctx = new Bunit.TestContext();
        ctx.JSInterop.Mode = Bunit.JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IStudioMapPackageDataSource>(data);
        ctx.Services.AddSingleton<IStudioMapStyleCatalogDataSource, UnsupportedStudioMapStyleCatalogDataSource>();

        var page = ctx.RenderComponent<StudioMapBuilderPage>();
        page.WaitForAssertion(() => FindButton(page, "Public works"), TimeSpan.FromSeconds(5));
        FindButton(page, "Public works").Click();
        page.WaitForAssertion(
            () => Assert.Contains("data-map-builder", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        FindButton(page, "Access").Click();
        page.WaitForAssertion(
            () => Assert.NotNull(page.Find("[data-publish-wizard]")),
            TimeSpan.FromSeconds(5));

        // Walk Validate → Dependencies → Visibility → Embed → Rollback → Confirm.
        foreach (var continueLabel in new[]
                 {
                     "Continue · Dependencies",
                     "Continue · Visibility",
                     "Continue · Embed",
                     "Continue · Rollback",
                     "Continue · Confirm",
                 })
        {
            FindButton(page, continueLabel).Click();
        }

        page.WaitForAssertion(
            () => Assert.NotNull(page.Find("[data-wizard-step='confirm']")),
            TimeSpan.FromSeconds(5));

        // The Confirm step's finish action publishes through the data source.
        page.FindAll("button.publish-wizard-finish").Single().Click();
        page.WaitForAssertion(() => Assert.True(published), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void MapBuilder_Inspector_StylePicker_BindsServerStyleIds_AndPreservesLegacyValue()
    {
        // The layer holds a legacy free-form style ("style:point-red") that the server does NOT advertise.
        // The inspector's style picker (#161) must render a <select> of the real server styleIds AND keep the
        // legacy value selected as a "(custom: …)" option so an existing saved map is never silently rewritten.
        var editor = ReadyEditor();
        editor.Layers[0].Style = "style:point-red";
        var data = new FakeMapDataSource
        {
            Workspace = new StudioMapWorkspace(
                [new StudioMapPackageListItem("map-1", "Public works", 1, 3, null, DateTimeOffset.UtcNow)],
                []),
            EditorLoad = new StudioMapEditorLoad(editor, [])
        };
        using var ctx = new Bunit.TestContext();
        ctx.JSInterop.Mode = Bunit.JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IStudioMapPackageDataSource>(data);
        ctx.Services.AddSingleton<IStudioMapStyleCatalogDataSource>(new StubStyleCatalog(new StudioMapStyleCatalog(
            [new StudioMapStyleOption("topographic", "Topographic"), new StudioMapStyleOption("night", null)],
            "topographic",
            null)));

        var page = ctx.RenderComponent<StudioMapBuilderPage>();
        page.WaitForAssertion(() => FindButton(page, "Public works"), TimeSpan.FromSeconds(5));
        FindButton(page, "Public works").Click();
        page.WaitForAssertion(
            () => Assert.NotNull(page.Find(".studio-style-picker")),
            TimeSpan.FromSeconds(5));

        // A real select drives the binding (the opaque free-form input is gone on the common path).
        var picker = page.Find(".studio-style-picker[data-style-picker-mode='select'] select");
        var optionValues = picker.QuerySelectorAll("option").Select(o => o.GetAttribute("value")).ToArray();
        Assert.Contains("topographic", optionValues);
        Assert.Contains("night", optionValues);

        // The legacy value survives as a selectable custom option, and stays the current selection.
        Assert.Contains("style:point-red", optionValues);
        Assert.Contains("(custom: style:point-red)", page.Markup, StringComparison.Ordinal);

        // Selecting a real server styleId commits it onto the layer.
        picker.Change("topographic");
        page.WaitForAssertion(
            () => Assert.Equal("topographic", editor.Layers[0].Style),
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void MapBuilder_Inspector_StylePicker_DegradesToTextInput_WhenCatalogUnbound()
    {
        // With no style catalog bound (unsupported / missing-binding), the picker degrades to the legacy
        // free-form text input so authoring still works against a server that does not expose /ogc/styles.
        var data = new FakeMapDataSource
        {
            Workspace = new StudioMapWorkspace(
                [new StudioMapPackageListItem("map-1", "Public works", 1, 3, null, DateTimeOffset.UtcNow)],
                []),
            EditorLoad = new StudioMapEditorLoad(ReadyEditor(), [])
        };
        using var ctx = new Bunit.TestContext();
        ctx.JSInterop.Mode = Bunit.JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IStudioMapPackageDataSource>(data);
        ctx.Services.AddSingleton<IStudioMapStyleCatalogDataSource, UnsupportedStudioMapStyleCatalogDataSource>();

        var page = ctx.RenderComponent<StudioMapBuilderPage>();
        page.WaitForAssertion(() => FindButton(page, "Public works"), TimeSpan.FromSeconds(5));
        FindButton(page, "Public works").Click();
        page.WaitForAssertion(
            () => Assert.NotNull(page.Find(".studio-style-picker[data-style-picker-mode='custom']")),
            TimeSpan.FromSeconds(5));

        Assert.NotNull(page.Find(".studio-style-picker[data-style-picker-mode='custom'] input"));
        Assert.Empty(page.FindAll(".studio-style-picker[data-style-picker-mode='select']"));
    }

    private sealed class StubStyleCatalog(StudioMapStyleCatalog catalog) : IStudioMapStyleCatalogDataSource
    {
        public Task<StudioMapStyleCatalog> GetStyleCatalogAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(catalog);
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

        public Func<StudioMapEditorState, StudioMapCommandResult>? OnPublish { get; set; }

        public Task<StudioMapWorkspace> GetWorkspaceAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Workspace);

        public Task<StudioMapEditorLoad> LoadAsync(string? mapId, CancellationToken cancellationToken = default) =>
            Task.FromResult(EditorLoad);

        public Task<StudioMapCommandResult> SaveDraftAsync(StudioMapEditorState state, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StudioMapCommandResult(true, "Saved.", state));

        public Task<StudioMapCommandResult> PublishAsync(StudioMapEditorState state, CancellationToken cancellationToken = default) =>
            Task.FromResult(OnPublish?.Invoke(state) ?? new StudioMapCommandResult(true, "Published.", state));

        public Task<StudioMapCommandResult> ReopenAsync(StudioMapEditorState state, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StudioMapCommandResult(true, "Reopened.", new StudioMapEditorState { MapId = state.MapId, Version = state.Version + 1 }));
    }
}
