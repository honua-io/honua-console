using AngleSharp.Dom;
using Bunit;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free render coverage for the Studio Map multiplayer collaboration surface (honua-console#124).
/// Asserts the StudioMapCollab + MapCommentThread mockup landmarks: the editor Comments/Activity tabs, the
/// presence chrome, the per-draft markup tool rail, feature-pinned comment pins + thread drawer, the
/// follow-mode pill, and the live activity sidebar — and that with no honua-server collaboration contract
/// bound (honua-server#1278) every live-data slot renders the explicit missing-binding state rather than
/// fabricated presence/cursors/comments (Console Patterns Charter section 11).
/// </summary>
public sealed class StudioMapCollaborationRenderTests
{
    [Fact]
    public void MapEditor_HasCommentsAndActivityTabs()
    {
        using var ctx = CreatePageContext(out _);
        var page = OpenEditor(ctx);

        var tabLabels = page.FindAll("nav.studio-map-tabs button").Select(button => button.TextContent.Trim()).ToList();
        Assert.Contains(tabLabels, label => label.StartsWith("Comments", StringComparison.Ordinal));
        Assert.Contains(tabLabels, label => label.StartsWith("Activity", StringComparison.Ordinal));
    }

    [Fact]
    public void ActivityTab_RendersPresenceChromeMarkupRailAndMissingBindingState()
    {
        using var ctx = CreatePageContext(out _);
        var page = OpenEditor(ctx);

        FindButton(page, "Activity").Click();
        page.WaitForAssertion(
            () => Assert.NotNull(page.Find("[data-collab-section='activity']")),
            TimeSpan.FromSeconds(5));

        // Presence chrome ("N here" + avatar stack + Invite) renders structurally even when unbound.
        Assert.NotNull(page.Find("[data-collab-presence]"));
        Assert.NotNull(page.Find("[data-collab-presence-empty]"));
        Assert.Contains("+ Invite", page.Markup, StringComparison.Ordinal);

        // The per-draft collaborative markup tool rail renders all six tools (disabled-pending while unbound).
        Assert.NotNull(page.Find("[data-collab-markup]"));
        Assert.Equal(6, page.FindAll("[data-collab-markup-tool]").Count);

        // The activity rail surfaces the explicit missing-binding state, never fabricated activity.
        var section = page.Find("[data-map-collab]");
        Assert.Equal("missing-binding", section.GetAttribute("data-map-collab"));
        Assert.Contains("Activity feed is not bound", page.Markup, StringComparison.Ordinal);
        Assert.NotNull(page.Find("section.console-state-missing"));
        Assert.Contains("honua-server#1278", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void CommentsTab_RendersMissingBindingState()
    {
        using var ctx = CreatePageContext(out _);
        var page = OpenEditor(ctx);

        FindButton(page, "Comments").Click();
        page.WaitForAssertion(
            () => Assert.NotNull(page.Find("[data-collab-section='comments']")),
            TimeSpan.FromSeconds(5));

        Assert.NotNull(page.Find("[data-collab-rail='comments']"));
        Assert.Contains("Comments are not bound", page.Markup, StringComparison.Ordinal);
        Assert.NotNull(page.Find("section.console-state-missing"));
    }

    [Fact]
    public void CollaborationPanel_WhenBound_RendersPresenceCursorsPinsFollowPillAndActivity()
    {
        // Drives the component directly with a bound session so the full mockup chrome (presence stack with
        // status dots, named cursors, comment pins, follow-mode pill, activity feed) is exercised, not just
        // the missing-binding state. This is Console-local test composition — never the merged runtime path.
        var session = BoundSession();
        using var ctx = new Bunit.BunitContext();
        ctx.Services.AddSingleton<IStudioMapCollaborationDataSource>(new StubCollaborationDataSource(session));

        var component = ctx.Render<StudioMapCollaboration>(parameters => parameters
            .Add(p => p.MapId, "map-1")
            .Add(p => p.Section, "activity"));

        component.WaitForAssertion(
            () => Assert.Equal("live", component.Find("[data-map-collab]").GetAttribute("data-map-collab")),
            TimeSpan.FromSeconds(5));

        // Presence: two avatars with the editing/commenting status dots.
        Assert.Equal(2, component.FindAll(".studio-map-collab-avatar").Count);
        Assert.NotNull(component.Find(".studio-map-collab-avatar-dot-editing"));
        Assert.NotNull(component.Find(".studio-map-collab-avatar-dot-commenting"));

        // Named live cursors + feature-pinned comment pins + the follow-mode pill.
        Assert.Single(component.FindAll("[data-collab-cursor]"));
        Assert.Single(component.FindAll("[data-collab-pin]"));
        Assert.NotNull(component.Find("[data-collab-follow]"));
        Assert.Contains("Following k.tan", component.Markup, StringComparison.Ordinal);

        // Live activity feed entry renders (no missing-binding state when bound).
        Assert.Contains("editing fill-opacity", component.Markup, StringComparison.Ordinal);
        Assert.Empty(component.FindAll("section.console-state-missing"));
    }

    [Fact]
    public void CollaborationPanel_WhenBound_OpensFeaturePinnedCommentThreadDrawer()
    {
        var session = BoundSession();
        using var ctx = new Bunit.BunitContext();
        ctx.Services.AddSingleton<IStudioMapCollaborationDataSource>(new StubCollaborationDataSource(session));

        var component = ctx.Render<StudioMapCollaboration>(parameters => parameters
            .Add(p => p.MapId, "map-1")
            .Add(p => p.Section, "comments"));

        component.WaitForAssertion(
            () => Assert.Single(component.FindAll("[data-collab-pin]")),
            TimeSpan.FromSeconds(5));

        // The thread list link and the canvas pin both open the comment-thread drawer.
        Assert.Empty(component.FindAll("[data-collab-drawer]"));
        component.Find("[data-collab-pin]").Click();
        component.WaitForAssertion(
            () => Assert.NotNull(component.Find("[data-collab-drawer]")),
            TimeSpan.FromSeconds(5));

        // The drawer carries the feature/layer anchor metadata and the thread messages.
        Assert.Contains("Parcel 04-021-204", component.Markup, StringComparison.Ordinal);
        Assert.Contains("anchored to", component.Markup, StringComparison.Ordinal);
        Assert.Contains("re-anchors if feature moves", component.Markup, StringComparison.Ordinal);
        Assert.Contains("reclassified to R-2", component.Markup, StringComparison.Ordinal);
        Assert.Contains("Mark resolved", component.Markup, StringComparison.Ordinal);
    }

    private static Bunit.BunitContext CreatePageContext(out FakeMapDataSource data)
    {
        var ctx = new Bunit.BunitContext();
        data = new FakeMapDataSource
        {
            Workspace = new StudioMapWorkspace(
                [new StudioMapPackageListItem("map-1", "Public works", 1, 3, null, DateTimeOffset.UtcNow)],
                []),
            EditorLoad = new StudioMapEditorLoad(ReadyEditor(), [])
        };
        ctx.Services.AddSingleton<IStudioMapPackageDataSource>(data);
        // The merged build registers only the unsupported collaboration source (no honua-server contract).
        ctx.Services.AddSingleton<IStudioMapCollaborationDataSource, UnsupportedStudioMapCollaborationDataSource>();
        // The map builder injects the style-picker catalog source (#161); the unsupported (no-server) impl
        // degrades the inspector to the legacy free-form style input.
        ctx.Services.AddSingleton<IStudioMapStyleCatalogDataSource, UnsupportedStudioMapStyleCatalogDataSource>();
        return ctx;
    }

    private static IRenderedComponent<StudioMapBuilderPage> OpenEditor(Bunit.BunitContext ctx)
    {
        var page = ctx.Render<StudioMapBuilderPage>();
        page.WaitForAssertion(() => FindButton(page, "Public works"), TimeSpan.FromSeconds(5));
        FindButton(page, "Public works").Click();
        page.WaitForAssertion(
            () => Assert.Contains("data-map-builder", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        return page;
    }

    private static IElement FindButton<TComponent>(IRenderedComponent<TComponent> page, string label)
        where TComponent : Microsoft.AspNetCore.Components.IComponent =>
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

    private static StudioMapCollaborationSession BoundSession()
    {
        var messages = new[]
        {
            new StudioMapCommentMessage("m1", "a.lee", "AL", "#1d6b3e", "14m ago",
                "this parcel was reclassified to R-2 last month — the source table is stale."),
            new StudioMapCommentMessage("m2", "jamie", "JD", "#ffe55c", "8m ago", "Confirmed — refreshing."),
        };
        return new StudioMapCollaborationSession(
            [
                new StudioMapPresenceParticipant("p1", "k.tan", "KT", "#2a6fdb", StudioMapPresenceActivity.Editing, "parcels/fill"),
                new StudioMapPresenceParticipant("p2", "a.lee", "AL", "#1d6b3e", StudioMapPresenceActivity.Commenting),
            ],
            [new StudioMapLiveCursor("p1", "k.tan", "#2a6fdb", 0.3, 0.4, "editing fill")],
            [new StudioMapCommentPin("thread_2f81", "Parcel 04-021-204", "parcels/fill", 2, false, 0.37, 0.52, messages)],
            [new StudioMapActivityEntry("k.tan", "KT", "#2a6fdb", "just now", "editing fill-opacity 0.85 → 0.88")],
            new StudioMapFollowState("p1", "k.tan", "KT", "#2a6fdb"),
            BindingState: null,
            RealtimeBound: true);
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
            Task.FromResult(new StudioMapCommandResult(true, "Reopened.", state));

        public Task<StudioMapGenerationOutcome> GenerateAsync(StudioMapEditorState currentState, StudioMapGenerationRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StudioMapGenerationOutcome { Status = StudioMapGenerationStatuses.Unsupported, Rationale = "Generation not configured for this test." });
    }

    private sealed class StubCollaborationDataSource : IStudioMapCollaborationDataSource
    {
        private readonly StudioMapCollaborationSession _session;

        public StubCollaborationDataSource(StudioMapCollaborationSession session) => _session = session;

        public Task<StudioMapCollaborationSession> GetSessionAsync(string? mapId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_session);

        public Task<StudioMapCommentPin?> GetThreadAsync(string? mapId, string threadId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_session.CommentPins.FirstOrDefault(pin => pin.ThreadId == threadId));
    }
}
