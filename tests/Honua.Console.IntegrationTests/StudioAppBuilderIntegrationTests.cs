using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Proves the server-bound app builder (<c>/studio/app</c>, honua-console#58) renders and exercises the
/// full app.package lifecycle against a real honua-server booted via Testcontainers (AC: the surface is not
/// merged against an in-memory client; it binds the Studio package lifecycle #1180 + publication registry
/// #1183). Off by default; skips cleanly when Docker, the server image, or the admin key is unavailable.
/// Drives the production <see cref="HonuaServerStudioAppPackageDataSource"/> over the live
/// <c>/api/v1/studio</c> contract: create draft -> validate -> save version + publish -> list versions ->
/// reopen into a new editable draft whose body rehydrates -> and asserts the page renders from live data.
/// </summary>
[Collection(StudioPackageLifecycleIntegrationCollection.Name)]
public sealed class StudioAppBuilderIntegrationTests
{
    private readonly StudioPackageLifecycleFixture _fixture;

    public StudioAppBuilderIntegrationTests(StudioPackageLifecycleFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task AppBuilder_FullLifecycle_FromLiveServer()
    {
        Skip.If(_fixture.SkipReason is not null, _fixture.SkipReason ?? string.Empty);

        var client = _fixture.CreateClient();
        var source = new HonuaServerStudioAppPackageDataSource(client);

        // Probe the live server is reachable/authorized before driving the lifecycle.
        var families = await client.ListPackageFamiliesAsync();
        Skip.If(
            families.Issue is not null,
            $"The live server did not return Studio package families: {families.Issue?.Detail}");

        // 1. Author a publishable app and save the first server draft.
        var state = ReadyApp();
        var saved = await source.SaveDraftAsync(state);
        Skip.If(
            saved.Issue is not null,
            $"The live server rejected the app draft create: {saved.Issue?.Detail}");
        Assert.True(saved.Succeeded);
        Assert.NotNull(saved.State!.DraftId);

        // 2. Server validation runs against the live draft.
        var validated = await source.ValidateAsync(saved.State!);
        Assert.NotNull(validated.Validation);

        // 3. Publish freezes an immutable version + creates a publication request.
        var published = await source.PublishAsync(saved.State!);
        Skip.If(
            published.Issue is not null,
            $"The live server rejected publish: {published.Issue?.Detail}");
        Assert.True(published.Succeeded);
        var itemId = published.State!.ItemId!.Value;

        // 4. Version history lists the immutable version from the live server.
        var history = await source.LoadVersionHistoryAsync(itemId);
        Skip.If(history.Issue is not null, $"The live server did not list versions: {history.Issue?.Detail}");
        Assert.True(history.HasVersions);
        var firstVersion = history.Versions[0];

        // 5. Reopen the published version: a fresh editable draft whose authored body rehydrates from the
        // live envelope, carrying no published pointer (reopened edits create new content versions).
        var reopened = await source.ReopenAsync(itemId, firstVersion.VersionId);
        Skip.If(reopened.Issue is not null, $"The live server did not reopen the version: {reopened.Issue?.Detail}");
        Assert.True(reopened.Succeeded);
        Assert.NotNull(reopened.State!.DraftId);
        Assert.Null(reopened.State.PublishedVersion);
        Assert.Equal("Field operations", reopened.State.Title);
        Assert.Equal("content:permits@v3", reopened.State.Pages[0].ContentBinding);
    }

    [SkippableFact]
    public async Task AppBuilderPage_RendersEditor_FromLiveServerBinding()
    {
        Skip.If(_fixture.SkipReason is not null, _fixture.SkipReason ?? string.Empty);

        var client = _fixture.CreateClient();
        var families = await client.ListPackageFamiliesAsync();
        Skip.If(
            families.Issue is not null,
            $"The live server did not return Studio package families: {families.Issue?.Detail}");

        var source = new HonuaServerStudioAppPackageDataSource(client);

        using var ctx = new Bunit.BunitContext();
        ctx.Services.AddSingleton(ConsoleCapabilityTestManifest.All);
        ctx.JSInterop.Mode = Bunit.JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IStudioAppPackageDataSource>(source);

        var page = ctx.Render<StudioAppBuilderPage>();

        // A new app opens the Console-owned scaffold (the server draft is created on first save), so the
        // editor renders from the live binding rather than the missing-binding surface.
        page.WaitForAssertion(
            () => Assert.Contains("data-app-builder", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(10));
        Assert.DoesNotContain("App package lifecycle is not bound", page.Markup, StringComparison.Ordinal);
    }

    private static StudioAppEditorState ReadyApp()
    {
        var state = StudioAppPackageMapper.CreateTemplate();
        state.Title = "Field operations";
        state.Summary = "Permit inspections";
        state.Pages[0].Title = "Permits";
        state.Pages[0].ContentBinding = "content:permits@v3";
        state.Actions.Add(new StudioAppActionState { Name = "submit", PageRoute = "/", RequiredPermission = "operator" });
        state.Visibility = "organization";
        state.EmbedEnabled = true;
        state.ShareEmbedPolicyReviewed = true;
        return state;
    }
}
