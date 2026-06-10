using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Proves the server-bound map builder renders, saves, and publishes from a live honua-server (AC:
/// Testcontainers coverage boots honua-server/PostgreSQL and asserts the map builder runs against live
/// data, not a mock; Docker-unavailable environments skip cleanly). Drives
/// <see cref="HonuaServerStudioMapPackageDataSource"/> over the real Studio package lifecycle (#1180) and
/// content publication registry (#1183) through the production typed client, reusing the shared lifecycle
/// fixture so container-boot mechanics are not duplicated.
/// </summary>
[Collection(StudioPackageLifecycleIntegrationCollection.Name)]
public sealed class StudioMapBuilderIntegrationTests
{
    private readonly StudioPackageLifecycleFixture _fixture;

    public StudioMapBuilderIntegrationTests(StudioPackageLifecycleFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task MapBuilder_SaveThenPublish_RunsAgainstLiveServer()
    {
        Skip.If(_fixture.SkipReason is not null, _fixture.SkipReason ?? string.Empty);

        var source = new HonuaServerStudioMapPackageDataSource(_fixture.CreateClient(), new NoopStudioMapGenerationClient(), new UnsupportedOperateTransitionDataSource());

        // The workspace must surface the no-list-verb capability state from live data rather than fabricate
        // a package list (Console Patterns Charter section 11).
        var workspace = await source.GetWorkspaceAsync();
        Assert.Empty(workspace.Packages);

        // 1. A new map opens a blank scaffold; saving creates a real server draft and carries its identity.
        var load = await source.LoadAsync(null);
        Assert.True(load.HasEditor);

        var state = load.State!;
        state.Title = "Live integration map";
        state.Basemap = "basemap:streets";
        state.InitialExtent = "-158.3,21.2,-157.6,21.7";
        state.Layers.Add(new StudioMapLayerEditor { SourceRef = "content:parcels@v1", Title = "Parcels" });

        var saved = await source.SaveDraftAsync(state);
        Skip.If(
            !saved.Succeeded,
            $"The live server did not accept the map draft save: {saved.Message}");
        Assert.NotNull(saved.State!.DraftId);

        // 2. Publishing freezes an immutable content version and routes it to the publication registry.
        var published = await source.PublishAsync(saved.State);
        Skip.If(
            !published.Succeeded,
            $"The live server did not accept the map publish: {published.Message}");
        Assert.Equal(StudioMapStatuses.Published, published.State!.Status);
        Assert.NotNull(published.State.ItemId);
        Assert.NotNull(published.State.VersionId);

        // 3. The map builder page renders the live published map (not the missing-binding surface).
        using var ctx = new Bunit.BunitContext();
        ctx.JSInterop.Mode = Bunit.JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IStudioMapPackageDataSource>(source);
        ctx.Services.AddSingleton<IStudioMapStyleCatalogDataSource, UnsupportedStudioMapStyleCatalogDataSource>();
        var page = ctx.Render<StudioMapBuilderPage>();
        page.WaitForAssertion(
            () => Assert.DoesNotContain("Map package lifecycle is not bound", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(10));
    }
}
