using Bunit;
using Honua.Console.Contracts;
using Honua.Console.Shell.Components.Studio;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free render coverage for the Studio home landing (issue #123), built to the StudioHome mockup in
/// docs/design-handoff/console-canvas/screens-studio.jsx. Asserts the hero prompt and the recent-projects
/// region across the live-bound, empty, and missing-binding states. Recent projects bind to
/// <see cref="IConsoleCatalogClient"/>; the missing-binding path is the explicit ConsoleStateView (Charter §11),
/// never seeded rows. The within-Studio content-type picker was removed in honua-console#203 (the omni-prompt AI
/// console infers the sub-type), so the home leads with the prompt + the "Ask Honua anything" omni entry instead
/// of an eight-card type grid.
/// </summary>
public sealed class StudioHomeRenderTests
{
    [Fact]
    public void StudioHome_RendersHeroPromptWithSuggestionChips()
    {
        using var ctx = NewContext(new StubCatalogClient([]));

        var page = ctx.Render<StudioHome>();

        page.WaitForAssertion(
            () => Assert.NotEmpty(page.FindAll("[data-studio-home-hero='true']")),
            TimeSpan.FromSeconds(5));

        Assert.Contains("What do you want to build?", page.Markup, StringComparison.Ordinal);
        Assert.NotEmpty(page.FindAll("textarea.studio-home-hero-input"));
        Assert.Equal(
            "studio-home-hero-heading",
            page.Find("textarea.studio-home-hero-input").GetAttribute("aria-labelledby"));

        // Every suggestion chip from the mockup is present.
        foreach (var suggestion in StudioHomeContentTypes.PromptSuggestions)
        {
            Assert.NotEmpty(page.FindAll($"[data-studio-suggestion=\"{suggestion}\"]"));
        }

        // Send targets the inline-authoring shell so it stays reachable from the prompt.
        var send = page.Find("[data-studio-home-send='true']");
        Assert.StartsWith("/studio/proof", send.GetAttribute("href"), StringComparison.Ordinal);
    }

    [Fact]
    public void StudioHome_ClickingSuggestion_SeedsHeroPromptAndSendHref()
    {
        using var ctx = NewContext(new StubCatalogClient([]));

        var page = ctx.Render<StudioHome>();
        page.WaitForAssertion(
            () => Assert.NotEmpty(page.FindAll("[data-studio-home-hero='true']")),
            TimeSpan.FromSeconds(5));

        var suggestion = StudioHomeContentTypes.PromptSuggestions[0];
        page.Find($"[data-studio-suggestion=\"{suggestion}\"]").Click();

        var send = page.Find("[data-studio-home-send='true']");
        Assert.Contains(Uri.EscapeDataString(suggestion), send.GetAttribute("href")!, StringComparison.Ordinal);
    }

    [Fact]
    public void StudioHome_DropsContentTypePicker_AndLeadsWithOmniPromptEntry()
    {
        // honua-console#203: the redundant within-Studio content-type selector is removed — the AI infers the
        // sub-type from the prompt. The home keeps the hero prompt and adds the omni-prompt AI console entry.
        using var ctx = NewContext(new StubCatalogClient([]));

        var page = ctx.Render<StudioHome>();
        page.WaitForAssertion(
            () => Assert.NotEmpty(page.FindAll("[data-studio-home-hero='true']")),
            TimeSpan.FromSeconds(5));

        // No content-type picker grid.
        Assert.Empty(page.FindAll("[data-content-type]"));
        Assert.Empty(page.FindAll("[data-studio-home-types='true']"));

        // The omni-prompt AI console is the single entry that replaces the type grid.
        var omni = page.Find("[data-studio-home-omni='true']");
        Assert.Equal("/studio/ai", omni.GetAttribute("href"));
    }

    [Fact]
    public void StudioHome_WhenCatalogBound_RendersRecentProjectsTable()
    {
        var items = new[]
        {
            Summary("map-1", "parcels-heatmap", "map", "Parcels heatmap (FY24)", CatalogSharingTiers.Public, modifiedDaysAgo: 0),
            Summary("dash-1", "q3-land-use", "dashboard", "Q3 land use review", CatalogSharingTiers.Private, modifiedDaysAgo: 1)
        };
        using var ctx = NewContext(new StubCatalogClient(items));

        var page = ctx.Render<StudioHome>();

        page.WaitForAssertion(
            () => Assert.NotEmpty(page.FindAll("[data-recent-project]")),
            TimeSpan.FromSeconds(5));

        Assert.NotEmpty(page.FindAll("[data-studio-home-recent='true']"));
        Assert.Contains("Recent projects", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Parcels heatmap (FY24)", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Q3 land use review", page.Markup, StringComparison.Ordinal);

        // Published vs draft status projects from the live access tier, not a fabricated signal.
        Assert.Contains("published", page.Markup, StringComparison.Ordinal);
        Assert.Contains("draft", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void StudioHome_ExcludesProjectsOlderThanFourteenDays()
    {
        var items = new[]
        {
            Summary("map-1", "recent-map", "map", "Recent map", CatalogSharingTiers.Private, modifiedDaysAgo: 2),
            Summary("map-2", "stale-map", "map", "Stale map", CatalogSharingTiers.Private, modifiedDaysAgo: 30)
        };
        using var ctx = NewContext(new StubCatalogClient(items));

        var page = ctx.Render<StudioHome>();

        page.WaitForAssertion(
            () => Assert.NotEmpty(page.FindAll("[data-recent-project]")),
            TimeSpan.FromSeconds(5));

        Assert.Contains("Recent map", page.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Stale map", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void StudioHome_WhenBoundButEmpty_RendersEmptyStateNotMockRows()
    {
        using var ctx = NewContext(new StubCatalogClient([]));

        var page = ctx.Render<StudioHome>();

        page.WaitForAssertion(
            () => Assert.NotEmpty(page.FindAll(".console-state-empty")),
            TimeSpan.FromSeconds(5));

        Assert.Contains("No recent projects", page.Markup, StringComparison.Ordinal);
        Assert.Empty(page.FindAll("[data-recent-project]"));
        // The hero + omni-prompt entry still render around the empty recent state.
        Assert.NotEmpty(page.FindAll("[data-studio-home-hero='true']"));
        Assert.NotEmpty(page.FindAll("[data-studio-home-omni='true']"));
    }

    [Fact]
    public void StudioHome_WhenNoServerBinding_RendersMissingBindingNotSeededData()
    {
        using var ctx = NewContext(new UnsupportedConsoleCatalogClient());

        var page = ctx.Render<StudioHome>();

        page.WaitForAssertion(
            () => Assert.NotEmpty(page.FindAll(".console-state-missing")),
            TimeSpan.FromSeconds(5));

        Assert.Contains("Connect an environment to see recent projects", page.Markup, StringComparison.Ordinal);
        Assert.NotEmpty(page.FindAll("a[href='/environments/new']"));
        Assert.Empty(page.FindAll("[data-recent-project]"));
        // The pure-UI hero + omni-prompt entry remain available even without a server binding.
        Assert.NotEmpty(page.FindAll("[data-studio-home-hero='true']"));
        Assert.NotEmpty(page.FindAll("[data-studio-home-omni='true']"));
    }

    [Fact]
    public void StudioPage_AtStudioRoot_RendersHomeLanding()
    {
        using var ctx = NewContext(new StubCatalogClient([]));
        ctx.Services.AddSingleton<IStudioAuthoringShell>(new ThrowingAuthoringShell());
        var nav = ctx.Services.GetRequiredService<Bunit.TestDoubles.BunitNavigationManager>();
        nav.NavigateTo("studio");

        var page = ctx.Render<StudioPage>();

        page.WaitForAssertion(
            () => Assert.NotEmpty(page.FindAll("[data-studio-home='true']")),
            TimeSpan.FromSeconds(5));

        // The home landing exposes the inline-authoring shell as a secondary entry.
        Assert.NotEmpty(page.FindAll("a[href='/studio/proof']"));
        // The content-type picker is gone; the omni-prompt AI console is the single entry (honua-console#203).
        Assert.Empty(page.FindAll("[data-content-type]"));
        Assert.NotEmpty(page.FindAll("[data-studio-home-omni='true']"));
    }

    private static Bunit.BunitContext NewContext(IConsoleCatalogClient catalog)
    {
        var ctx = new Bunit.BunitContext();
        ctx.Services.AddSingleton(catalog);
        ctx.Services.AddSingleton<IConsoleCatalogReadContextResolver>(new StubReadContextResolver());
        return ctx;
    }

    private static ConsoleContentSummary Summary(
        string id,
        string slug,
        string type,
        string title,
        string sharing,
        int modifiedDaysAgo) =>
        new()
        {
            Id = id,
            Slug = slug,
            Type = type,
            Title = title,
            Owner = "jamie",
            Access = new ConsoleShareAccess { Sharing = sharing },
            Modified = DateTimeOffset.UtcNow.AddDays(-modifiedDaysAgo)
        };

    private sealed class StubReadContextResolver : IConsoleCatalogReadContextResolver
    {
        public Task<CatalogReadContext> ResolveAsync(string? publicLinkToken, CancellationToken cancellationToken = default) =>
            Task.FromResult(CatalogReadContext.Authenticated);
    }

    private sealed class StubCatalogClient(IReadOnlyList<ConsoleContentSummary> items) : IConsoleCatalogClient
    {
        public Task<CatalogSearchResult> SearchAsync(CatalogListRequest request, CatalogReadContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CatalogSearchResult(items, new Dictionary<string, int>(StringComparer.Ordinal), request));

        public Task<CatalogItemReadResult> GetCatalogItemAsync(string idOrSlug, CatalogReadContext context, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<CatalogItemReadResult> GetOpenDataItemAsync(string idOrSlug, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<MapPackageReadResult> GetMapPackageAsync(string mapId, CatalogReadContext context, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<MapPackageReadResult> GetDraftMapAsync(string sourceItemId, CatalogReadContext context, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<MapPackageReadResult> AuthorizeEmbedAsync(string mapId, EmbedRouteOptions options, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class ThrowingAuthoringShell : IStudioAuthoringShell
    {
        public Task<StudioAuthoringSession> CreateInitialSessionAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The Studio home landing must not read the authoring shell session.");

        public Task<StudioAuthoringSession> SelectWorkflowAsync(StudioAuthoringSession session, string workflowId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException();

        public Task<StudioAuthoringSession> GeneratePackageAsync(StudioAuthoringSession session, string workflowId, string prompt, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException();

        public Task<StudioAuthoringSession> ApplyClarificationAsync(StudioAuthoringSession session, string questionId, string choiceId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException();

        public Task<StudioAuthoringSession> ValidateAsync(StudioAuthoringSession session, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException();

        public Task<StudioAuthoringSession> PreviewPlanAsync(StudioAuthoringSession session, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException();

        public Task<StudioAuthoringSession> SaveVersionAsync(StudioAuthoringSession session, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException();

        public Task<StudioAuthoringSession> PublishAsync(StudioAuthoringSession session, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException();
    }
}
