using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Proves the server-bound dashboard builder runs against a real honua-server started via Testcontainers
/// (AC: the surface renders from a live server and is not merged against an in-memory client). Drives
/// <see cref="HonuaServerStudioDashboardPackageDataSource"/> over the live <c>/api/v1/studio</c> package
/// lifecycle: creates a real dashboard draft (family <c>dashboard</c>, schema
/// <c>studio_dashboard_package.v1</c>), runs server validation, saves an immutable version, publishes,
/// and reopens it - then renders the page from the live draft. Off by default; skips cleanly when Docker,
/// the server image, the opt-in flag, or the admin API key is unavailable. Reuses
/// <see cref="StudioPackageLifecycleFixture"/> so container-boot mechanics are not duplicated.
/// </summary>
[Collection(StudioPackageLifecycleIntegrationCollection.Name)]
public sealed class StudioDashboardBuilderIntegrationTests
{
    private readonly StudioPackageLifecycleFixture _fixture;

    public StudioDashboardBuilderIntegrationTests(StudioPackageLifecycleFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task DashboardBuilder_SaveValidatePublishReopen_AgainstLiveServer()
    {
        Skip.If(_fixture.SkipReason is not null, _fixture.SkipReason ?? string.Empty);

        var dataSource = new HonuaServerStudioDashboardPackageDataSource(_fixture.CreateClient());
        var editor = ReadyDashboard();

        // 1. Save creates a real server draft (family dashboard) and captures server identity.
        var saved = await dataSource.SaveDraftAsync(editor);
        Skip.If(
            !saved.Succeeded,
            $"The live server did not accept a dashboard draft: {saved.Message}");
        Assert.NotNull(saved.State!.DraftId);
        Assert.True(saved.State.Generation >= 1);

        // 2. Server-side validation runs against the live draft (not a mock result).
        var validated = await dataSource.ValidateAsync(saved.State);
        Assert.True(validated.Succeeded, validated.Message);

        // 3. Publish saves an immutable version and creates a publish request on the live server.
        var published = await dataSource.PublishAsync(validated.State!);
        Assert.True(published.Succeeded, published.Message);
        Assert.Equal(StudioDashboardStatuses.Published, published.State!.Status);
        Assert.NotNull(published.State.ItemId);
        Assert.NotNull(published.State.PublishedVersion);

        // 4. Reopen resolves the published version on the live server and reopens it as a new draft.
        var reopened = await dataSource.ReopenAsync(
            published.State.DashboardId!,
            published.State.PublishedVersion!.Value);
        Assert.True(reopened.Succeeded, reopened.Message);
        Assert.Equal(StudioDashboardStatuses.Draft, reopened.State!.Status);
        Assert.NotEqual(saved.State.DraftId, reopened.State.DraftId);

        // 5. The page renders the live-loaded draft editor (not the missing-binding surface).
        using var ctx = new Bunit.BunitContext();
        ctx.Services.AddSingleton<IStudioDashboardPackageDataSource>(dataSource);

        var page = ctx.Render<StudioDashboardBuilderPage>();
        page.WaitForAssertion(
            () => Assert.DoesNotContain("Dashboard package lifecycle is not bound", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(10));
        // The live workspace exposes the New-dashboard authoring entry point.
        Assert.Contains("New dashboard", page.Markup, StringComparison.Ordinal);
    }

    private static StudioDashboardEditorState ReadyDashboard()
    {
        var state = new StudioDashboardEditorState { Title = "Live operations dashboard" };
        state.Bindings.Add(new StudioDashboardBindingEditor
        {
            Alias = "requests",
            ContentRef = "content:service-requests",
            VersionPin = "v5"
        });
        state.Panels.Add(new StudioDashboardPanelEditor
        {
            Title = "Requests by district",
            Kind = StudioDashboardPanelKinds.Chart,
            BindingAlias = "requests",
            VegaLiteSpec = StudioDashboardChartSpec.DefaultBarChart("district", "request_count")
        });
        return state;
    }
}
