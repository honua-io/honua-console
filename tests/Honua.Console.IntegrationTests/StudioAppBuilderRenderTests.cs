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
        using var ctx = new Bunit.TestContext();
        ctx.Services.AddSingleton<IStudioAppPackageDataSource>(data);

        var page = ctx.RenderComponent<StudioAppBuilderPage>();

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
        using var ctx = new Bunit.TestContext();
        ctx.Services.AddSingleton<IStudioAppPackageDataSource>(data);

        var page = ctx.RenderComponent<StudioAppBuilderPage>();

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
    public void AppBuilder_ReadyApp_EnablesPublishAndShowsShareReview()
    {
        var data = new FakeAppDataSource
        {
            Load = new StudioAppEditorLoad(ReadyApp(), [])
        };
        using var ctx = new Bunit.TestContext();
        ctx.Services.AddSingleton<IStudioAppPackageDataSource>(data);

        var page = ctx.RenderComponent<StudioAppBuilderPage>();

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
        using var ctx = new Bunit.TestContext();
        ctx.Services.AddSingleton<IStudioAppPackageDataSource>(data);

        var page = ctx.RenderComponent<StudioAppBuilderPage>();

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
        using var ctx = new Bunit.TestContext();
        ctx.Services.AddSingleton<IStudioAppPackageDataSource>(data);

        var page = ctx.RenderComponent<StudioAppBuilderPage>();
        page.WaitForAssertion(
            () => Assert.False(FindButton(page, "Preview").HasAttribute("disabled")),
            TimeSpan.FromSeconds(5));

        FindButton(page, "Preview").Click();

        page.WaitForAssertion(
            () => Assert.Contains("Preview plan ready", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.True(data.PreviewCalled);
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

        public Task<StudioAppEditorLoad> LoadAsync(Guid? draftId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Load);

        public Task<StudioAppCommandResult> SaveDraftAsync(StudioAppEditorState state, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StudioAppCommandResult(true, "Saved.", state));

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
    }
}
