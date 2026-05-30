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
/// docs/design-handoff/console-canvas/screens-studio.jsx. Asserts the hero prompt, the eight content-type
/// cards (each linking to its existing builder route), and the recent-projects region across the live-bound,
/// empty, and missing-binding states. Recent projects bind to <see cref="IConsoleCatalogClient"/>; the
/// missing-binding path is the explicit ConsoleStateView (Charter §11), never seeded rows.
/// </summary>
public sealed class StudioHomeRenderTests
{
    private static readonly IReadOnlyDictionary<string, string> ExpectedRoutes = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["map"] = "/studio/map",
        ["dashboard"] = "/studio/dashboard",
        ["report"] = "/studio/report",
        ["form"] = "/studio/form",
        ["app"] = "/studio/app",
        ["query"] = "/studio/query",
        ["analysis"] = "/studio/analysis",
        ["workflow"] = "/studio/workflows/new"
    };

    [Fact]
    public void StudioHome_RendersHeroPromptWithSuggestionChips()
    {
        using var ctx = NewContext(new StubCatalogClient([]));

        var page = ctx.RenderComponent<StudioHome>();

        page.WaitForAssertion(
            () => Assert.NotEmpty(page.FindAll("[data-studio-home-hero='true']")),
            TimeSpan.FromSeconds(5));

        Assert.Contains("What do you want to build?", page.Markup, StringComparison.Ordinal);
        Assert.NotEmpty(page.FindAll("textarea.studio-home-hero-input"));

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

        var page = ctx.RenderComponent<StudioHome>();
        page.WaitForAssertion(
            () => Assert.NotEmpty(page.FindAll("[data-studio-home-hero='true']")),
            TimeSpan.FromSeconds(5));

        var suggestion = StudioHomeContentTypes.PromptSuggestions[0];
        page.Find($"[data-studio-suggestion=\"{suggestion}\"]").Click();

        var send = page.Find("[data-studio-home-send='true']");
        Assert.Contains(Uri.EscapeDataString(suggestion), send.GetAttribute("href")!, StringComparison.Ordinal);
    }

    [Fact]
    public void StudioHome_RendersEightContentTypeCards_LinkingToExistingBuilderRoutes()
    {
        using var ctx = NewContext(new StubCatalogClient([]));

        var page = ctx.RenderComponent<StudioHome>();
        page.WaitForAssertion(
            () => Assert.NotEmpty(page.FindAll("[data-studio-home-types='true']")),
            TimeSpan.FromSeconds(5));

        var cards = page.FindAll("[data-content-type]");
        Assert.Equal(8, cards.Count);

        foreach (var (key, route) in ExpectedRoutes)
        {
            var card = page.Find($"[data-content-type='{key}']");
            Assert.Equal(route, card.GetAttribute("href"));
        }
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

        var page = ctx.RenderComponent<StudioHome>();

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

        var page = ctx.RenderComponent<StudioHome>();

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

        var page = ctx.RenderComponent<StudioHome>();

        page.WaitForAssertion(
            () => Assert.NotEmpty(page.FindAll(".console-state-empty")),
            TimeSpan.FromSeconds(5));

        Assert.Contains("No recent projects", page.Markup, StringComparison.Ordinal);
        Assert.Empty(page.FindAll("[data-recent-project]"));
        // The home grid and hero still render around the empty recent state.
        Assert.NotEmpty(page.FindAll("[data-content-type]"));
    }

    [Fact]
    public void StudioHome_WhenNoServerBinding_RendersMissingBindingNotSeededData()
    {
        using var ctx = NewContext(new UnsupportedConsoleCatalogClient());

        var page = ctx.RenderComponent<StudioHome>();

        page.WaitForAssertion(
            () => Assert.NotEmpty(page.FindAll(".console-state-missing")),
            TimeSpan.FromSeconds(5));

        Assert.Contains("Recent projects are not bound", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Honua:Server:BaseUrl", page.Markup, StringComparison.Ordinal);
        Assert.Empty(page.FindAll("[data-recent-project]"));
        // The pure-UI hero + content-type grid remain available even without a server binding.
        Assert.Equal(8, page.FindAll("[data-content-type]").Count);
    }

    [Fact]
    public void StudioPage_AtStudioRoot_RendersHomeLanding()
    {
        using var ctx = NewContext(new StubCatalogClient([]));
        ctx.Services.AddSingleton<IStudioAuthoringShell>(new ThrowingAuthoringShell());
        var nav = ctx.Services.GetRequiredService<Bunit.TestDoubles.FakeNavigationManager>();
        nav.NavigateTo("studio");

        var page = ctx.RenderComponent<StudioPage>();

        page.WaitForAssertion(
            () => Assert.NotEmpty(page.FindAll("[data-studio-home='true']")),
            TimeSpan.FromSeconds(5));

        // The home landing exposes the inline-authoring shell as a secondary entry.
        Assert.NotEmpty(page.FindAll("a[href='/studio/proof']"));
        Assert.Equal(8, page.FindAll("[data-content-type]").Count);
    }

    private static Bunit.TestContext NewContext(IConsoleCatalogClient catalog)
    {
        var ctx = new Bunit.TestContext();
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
