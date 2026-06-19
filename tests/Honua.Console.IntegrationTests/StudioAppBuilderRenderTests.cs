using AngleSharp.Dom;
using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free render coverage for the app builder page (<c>/studio/app</c>, honua-console#58). Verifies the
/// missing-binding surface, that the editor renders authored pages/actions and the explicit share/embed
/// review (AC: share/embed policy explicit before publish), that Publish stays gated until a page binds
/// content, an action declares a permission, and the policy is reviewed, and that an opened existing app
/// exposes the version-history reopen/rollback affordances (AC: reopened edits create new content versions).
/// Drives the page through a fake <see cref="IStudioAppPackageDataSource"/> rather than a mock server.
/// </summary>
public sealed class StudioAppBuilderRenderTests
{
    [Fact]
    public void AppBuilder_WhenBindingMissing_RendersNotBoundSurface()
    {
        var data = new FakeAppDataSource
        {
            Load = new StudioAppEditorLoad(null, [MissingBinding])
        };
        using var ctx = new Bunit.BunitContext();
        ctx.JSInterop.Mode = Bunit.JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IStudioAppPackageDataSource>(data);

        var page = ctx.Render<StudioAppBuilderPage>();

        page.WaitForAssertion(
            () => Assert.Contains("App package lifecycle is not bound", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.DoesNotContain("data-app-builder", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void AppBuilder_NewTemplate_GatesPublishUntilRequirementsResolved()
    {
        var data = new FakeAppDataSource
        {
            Load = new StudioAppEditorLoad(StudioAppPackageMapper.CreateTemplate(), [])
        };
        using var ctx = new Bunit.BunitContext();
        ctx.JSInterop.Mode = Bunit.JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IStudioAppPackageDataSource>(data);

        var page = ctx.Render<StudioAppBuilderPage>();

        page.WaitForAssertion(
            () => Assert.Contains("data-app-builder", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        // The blank template is unbound and unreviewed: Publish is gated and the pre-publish gate lists the
        // unmet requirements (explicit share/embed review among them).
        Assert.True(FindButton(page, "Publish").HasAttribute("disabled"));
        Assert.Contains("Resolve before publish", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Review the share/embed policy", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void AppBuilder_Editor_RendersThreePaneStructureFromDesign()
    {
        // The StudioAppEditor handoff is a three-pane editor: an app-identity toolbar (name + lifecycle
        // badge + summary stats + preview/publish actions), a page tree + component palette rail, a
        // responsive page canvas, and a component-binding inspector. Assert each region renders.
        var data = new FakeAppDataSource
        {
            Load = new StudioAppEditorLoad(ReadyApp(), [])
        };
        using var ctx = new Bunit.BunitContext();
        ctx.JSInterop.Mode = Bunit.JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IStudioAppPackageDataSource>(data);

        var page = ctx.Render<StudioAppBuilderPage>();

        page.WaitForAssertion(
            () => Assert.Single(page.FindAll("[data-app-toolbar]")),
            TimeSpan.FromSeconds(5));

        // Toolbar: app-identity stats line ("1 page · 1 action · organization · embeddable") + lifecycle actions.
        Assert.Single(page.FindAll("[data-app-stats]"));
        Assert.Contains("1 page", page.Find("[data-app-stats]").TextContent, StringComparison.Ordinal);
        Assert.Contains("1 action", page.Find("[data-app-stats]").TextContent, StringComparison.Ordinal);

        // Left rail: the page tree (one node per page) and the component palette listing every component kind.
        Assert.Single(page.FindAll("[data-app-page-tree]"));
        Assert.Single(page.FindAll("[data-app-rail] .studio-app-page-node"));
        var palette = page.Find("[data-app-palette]");
        foreach (var kind in StudioAppComponentKinds.All)
        {
            Assert.Contains(kind, palette.TextContent, StringComparison.Ordinal);
        }

        // Center canvas: a responsive app frame with the selected page's component binding.
        Assert.Single(page.FindAll("[data-app-canvas]"));
        var frame = page.Find("[data-app-frame]");
        Assert.Contains("map · selected", frame.TextContent, StringComparison.Ordinal);
        Assert.Contains("content:permits@v3", frame.TextContent, StringComparison.Ordinal);

        // Right inspector: the per-page component-binding inspector, the app details, actions, and share policy.
        Assert.Single(page.FindAll("[data-app-inspector]"));
        Assert.Single(page.FindAll("[data-app-page-inspector]"));
        Assert.Single(page.FindAll("[data-app-details]"));
        Assert.Single(page.FindAll("[data-app-actions]"));
        Assert.Single(page.FindAll("[data-app-share]"));
    }

    [Fact]
    public void AppBuilder_SelectingPageInTree_UpdatesCanvasAndInspector()
    {
        var state = StudioAppPackageMapper.CreateTemplate();
        state.Title = "Field operations";
        state.Pages[0].Route = "/home";
        state.Pages[0].Title = "Home";
        state.Pages[0].ContentBinding = "content:home@v1";
        state.Pages.Add(new StudioAppPageState
        {
            Route = "/inspect",
            Title = "Inspect a site",
            ComponentKind = "form",
            ContentBinding = "content:inspection@v4"
        });
        var data = new FakeAppDataSource { Load = new StudioAppEditorLoad(state, []) };
        using var ctx = new Bunit.BunitContext();
        ctx.JSInterop.Mode = Bunit.JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IStudioAppPackageDataSource>(data);

        var page = ctx.Render<StudioAppBuilderPage>();
        page.WaitForAssertion(
            () => Assert.Equal(2, page.FindAll("[data-app-rail] .studio-app-page-node").Count),
            TimeSpan.FromSeconds(5));

        // First page is selected by default; canvas shows its map binding.
        Assert.Contains("content:home@v1", page.Find("[data-app-frame]").TextContent, StringComparison.Ordinal);

        // Selecting the second page node repaints the canvas/inspector with the form component binding.
        page.FindAll("[data-app-rail] .studio-app-page-node")[1].Click();
        page.WaitForAssertion(
            () => Assert.Contains("content:inspection@v4", page.Find("[data-app-frame]").TextContent, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Contains("form · selected", page.Find("[data-app-frame]").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void AppBuilder_ReadyApp_EnablesPublishAndShowsShareReview()
    {
        var data = new FakeAppDataSource
        {
            Load = new StudioAppEditorLoad(ReadyApp(), [])
        };
        using var ctx = new Bunit.BunitContext();
        ctx.JSInterop.Mode = Bunit.JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IStudioAppPackageDataSource>(data);

        var page = ctx.Render<StudioAppBuilderPage>();

        page.WaitForAssertion(
            () => Assert.False(FindButton(page, "Publish").HasAttribute("disabled")),
            TimeSpan.FromSeconds(5));
        Assert.Contains("Share/embed policy", page.Markup, StringComparison.Ordinal);
        Assert.Contains("I reviewed the share/embed policy", page.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Resolve before publish", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void AppBuilder_ExistingItem_RendersVersionHistoryWithReopenAndRollback()
    {
        var itemId = Guid.NewGuid();
        var existing = ReadyApp();
        existing.DraftId = Guid.NewGuid();
        existing.ItemId = itemId;
        existing.PublishedVersion = 2;

        var data = new FakeAppDataSource
        {
            Load = new StudioAppEditorLoad(existing, []),
            History = new StudioAppVersionHistory(itemId,
            [
                new StudioAppVersionItem(Guid.NewGuid(), 2, "Second", IsPublished: true, IsCurrent: true, DateTimeOffset.UtcNow),
                new StudioAppVersionItem(Guid.NewGuid(), 1, "First", IsPublished: false, IsCurrent: false, DateTimeOffset.UtcNow.AddDays(-1))
            ])
        };
        using var ctx = new Bunit.BunitContext();
        ctx.JSInterop.Mode = Bunit.JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IStudioAppPackageDataSource>(data);

        var page = ctx.Render<StudioAppBuilderPage>();

        page.WaitForAssertion(
            () => Assert.Contains("data-app-history", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        Assert.Contains("Version history", page.Markup, StringComparison.Ordinal);
        // Each version offers Reopen; the older (non-published) version also offers rollback.
        Assert.Equal(2, page.FindAll("button").Count(button => button.TextContent.Contains("Reopen as draft", StringComparison.Ordinal)));
        Assert.Contains(page.FindAll("button"), button => button.TextContent.Contains("Roll back to this", StringComparison.Ordinal));

        // Reopening a published version drives the data source and re-renders the new editable draft.
        page.FindAll("button").First(button => button.TextContent.Contains("Reopen as draft", StringComparison.Ordinal)).Click();
        page.WaitForAssertion(
            () => Assert.Contains("Reopened", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.True(data.ReopenCalled);
    }

    [Fact]
    public void AppBuilder_Preview_DrivesDataSourceAndShowsPlanMessage()
    {
        var existing = ReadyApp();
        existing.DraftId = Guid.NewGuid();

        var data = new FakeAppDataSource
        {
            Load = new StudioAppEditorLoad(existing, []),
            PreviewResult = new StudioAppCommandResult(true, "Preview plan ready (inline). Steps: validate-envelope.", existing)
        };
        using var ctx = new Bunit.BunitContext();
        ctx.JSInterop.Mode = Bunit.JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IStudioAppPackageDataSource>(data);

        var page = ctx.Render<StudioAppBuilderPage>();
        page.WaitForAssertion(
            () => Assert.False(FindButton(page, "Live preview").HasAttribute("disabled")),
            TimeSpan.FromSeconds(5));

        FindButton(page, "Live preview").Click();

        page.WaitForAssertion(
            () => Assert.Contains("Preview plan ready", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.True(data.PreviewCalled);
    }

    [Fact]
    public void AppBuilder_NewFromPrompt_RendersConversationAndLivePreview()
    {
        var data = new FakeAppDataSource
        {
            Load = new StudioAppEditorLoad(ReadyApp(), [])
        };
        using var ctx = new Bunit.BunitContext();
        ctx.JSInterop.Mode = Bunit.JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IStudioAppPackageDataSource>(data);

        var page = ctx.Render<StudioAppBuilderPage>();
        page.WaitForAssertion(() => FindButton(page, "New from prompt"), TimeSpan.FromSeconds(5));
        FindButton(page, "New from prompt").Click();

        // StudioAppAI: a conversation column + a live app-package preview driven by the real draft.
        page.WaitForAssertion(
            () => Assert.Equal("ai", page.Find("[data-app-builder]").GetAttribute("data-app-view")),
            TimeSpan.FromSeconds(5));

        Assert.NotNull(page.Find("[data-studio-ai-pane]"));
        Assert.Contains("App from prompt", page.Markup, StringComparison.Ordinal);

        // The conversation seeds a neutral intro turn inviting the author to describe the app; the proposal is
        // server-grounded and validated, never fabricated.
        Assert.Contains("Describe the app you want", page.Markup, StringComparison.Ordinal);

        // The live preview reflects the real draft's bound page (the seeded map page).
        var preview = page.Find("[data-app-preview]");
        Assert.Contains("content:permits@v3", preview.TextContent, StringComparison.Ordinal);
        Assert.NotEmpty(page.FindAll("[data-preview-kind=\"map\"]"));

        // The preview titlebar offers "Open editor" which returns to the editor view.
        FindButton(page, "Open editor →").Click();
        Assert.Equal("editor", page.Find("[data-app-builder]").GetAttribute("data-app-view"));
    }

    [Fact]
    public void AppBuilder_RefinePrompt_ForwardsToServerGenerationAndRendersResult()
    {
        var data = new FakeAppDataSource
        {
            Load = new StudioAppEditorLoad(StudioAppPackageMapper.CreateTemplate(), []),
            // A refine prompt drives the real server generation contract (not SaveDraftAsync). Return a
            // generated outcome whose proposed app hydrates the editor and surfaces "view evidence".
            OnGenerate = state =>
            {
                state.Title = "Field operations";
                state.Pages[0].ContentBinding = "content:permits@v3";
                return new StudioAppGenerationOutcome
                {
                    Status = StudioAppGenerationStatuses.Generated,
                    State = state,
                    Rationale = "Proposed a single-page operations app bound to permits."
                };
            }
        };
        using var ctx = new Bunit.BunitContext();
        // A generated outcome marks the editor dirty, so the UnsavedChangesGuard syncs its beforeunload
        // handler over JS interop; loose mode lets that no-op in the renderer harness.
        ctx.JSInterop.Mode = Bunit.JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IStudioAppPackageDataSource>(data);

        var page = ctx.Render<StudioAppBuilderPage>();
        page.WaitForAssertion(() => FindButton(page, "New from prompt"), TimeSpan.FromSeconds(5));
        FindButton(page, "New from prompt").Click();
        page.WaitForAssertion(() => page.Find(".studio-ai-refine-input"), TimeSpan.FromSeconds(5));

        page.Find(".studio-ai-refine-input").Input("Build an app for permit inspections");
        page.Find(".studio-ai-send").Click();

        // The prompt is echoed and the server-produced result renders (rationale + a "view evidence" turn);
        // nothing is saved to a draft on a generate turn (SaveDraftAsync is the explicit save action).
        page.WaitForAssertion(
            () => Assert.Contains("view evidence", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Contains("Build an app for permit inspections", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Proposed a single-page operations app bound to permits.", page.Markup, StringComparison.Ordinal);
        Assert.Equal(1, data.GenerateCount);
        Assert.Equal(0, data.SaveCount);
    }

    private static IElement FindButton(IRenderedComponent<StudioAppBuilderPage> page, string label) =>
        page.FindAll("button").First(button => button.TextContent.Contains(label, StringComparison.Ordinal));

    private static StudioAppEditorState ReadyApp()
    {
        var state = StudioAppPackageMapper.CreateTemplate();
        state.Title = "Field operations";
        state.Pages[0].ContentBinding = "content:permits@v3";
        state.Actions.Add(new StudioAppActionState { Name = "submit", PageRoute = "/", RequiredPermission = "operator" });
        state.Visibility = "organization";
        state.EmbedEnabled = true;
        state.ShareEmbedPolicyReviewed = true;
        return state;
    }

    private static readonly StudioAppCapabilityState MissingBinding = new(
        "App builder",
        "Missing binding",
        "Honua:Server:BaseUrl",
        "Configure Honua:Server:BaseUrl so the app builder can bind the server-owned Studio package lifecycle.");

    private sealed class FakeAppDataSource : IStudioAppPackageDataSource
    {
        public StudioAppEditorLoad Load { get; set; } = new(null, []);

        public StudioAppVersionHistory History { get; set; } = new(Guid.Empty, []);

        public StudioAppCommandResult PreviewResult { get; set; } = new(true, "Preview ready.");

        public bool ReopenCalled { get; private set; }

        public bool PreviewCalled { get; private set; }

        public int SaveCount { get; private set; }

        public Task<StudioAppEditorLoad> LoadAsync(Guid? draftId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Load);

        public Task<StudioAppCommandResult> SaveDraftAsync(StudioAppEditorState state, CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return Task.FromResult(new StudioAppCommandResult(true, "Saved.", state));
        }

        public Task<StudioAppCommandResult> ValidateAsync(StudioAppEditorState state, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StudioAppCommandResult(true, "Valid.", state, new StudioAppValidationView(true, [])));

        public Task<StudioAppCommandResult> PublishAsync(StudioAppEditorState state, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StudioAppCommandResult(true, "Published.", state));

        public Task<StudioAppCommandResult> PreviewAsync(StudioAppEditorState state, CancellationToken cancellationToken = default)
        {
            PreviewCalled = true;
            return Task.FromResult(PreviewResult);
        }

        public Task<StudioAppVersionHistory> LoadVersionHistoryAsync(Guid itemId, CancellationToken cancellationToken = default) =>
            Task.FromResult(History);

        public Task<StudioAppCommandResult> ReopenAsync(Guid itemId, Guid versionId, CancellationToken cancellationToken = default)
        {
            ReopenCalled = true;
            var draft = StudioAppPackageMapper.CreateTemplate();
            draft.DraftId = Guid.NewGuid();
            draft.ItemId = itemId;
            return Task.FromResult(new StudioAppCommandResult(true, "Reopened as new draft.", draft));
        }

        public Task<StudioAppCommandResult> RollbackAsync(Guid itemId, Guid targetVersionId, string? reason, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StudioAppCommandResult(true, "Rolled back."));

        public Func<StudioAppEditorState, StudioAppGenerationOutcome>? OnGenerate { get; set; }

        public int GenerateCount { get; private set; }

        public Task<StudioAppGenerationOutcome> GenerateAsync(StudioAppEditorState currentState, StudioAppGenerationRequest request, CancellationToken cancellationToken = default)
        {
            GenerateCount++;
            return Task.FromResult(OnGenerate?.Invoke(currentState)
                ?? new StudioAppGenerationOutcome { Status = StudioAppGenerationStatuses.Unsupported, Rationale = "Generation not configured for this test." });
        }
    }
}
