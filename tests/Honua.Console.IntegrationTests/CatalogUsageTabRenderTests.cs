using Bunit;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free render coverage for the Catalog item Usage tab (UI-011): the pre-retirement blast-radius /
/// impact view. The honua-server Console metadata v2 content API (honua-server#1162) exposes no
/// dependency/usage endpoint, so the server-bound detail leaves ConsoleContentDetail.UsageBound false and
/// the Usage tab must render an explicit missing-binding state — never a falsely-reassuring "no consumers"
/// empty state (Console Patterns Charter section 11). When a usage source binds (UsageBound true), the live
/// closure renders. Drives CatalogDetailPage through a stub catalog client (never a mock server).
/// </summary>
public sealed class CatalogUsageTabRenderTests
{
    [Fact]
    public void UsageTab_WhenUsageUnbound_RendersMissingBindingNotEmptyState()
    {
        // Mirrors the server-bound mapper: Usage empty AND UsageBound false (no dependency endpoint).
        var detail = Detail(usageBound: false, usage: []);

        var page = RenderUsageTab(detail);

        page.WaitForAssertion(
            () => Assert.Contains("Dependency usage is not bound", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Contains("honua-server#1162", page.Markup, StringComparison.Ordinal);
        // It must NOT claim there are no downstream consumers when the server never reported any.
        Assert.DoesNotContain("No downstream consumers", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void UsageTab_WhenUsageBoundButEmpty_RendersHonestNoConsumersEmptyState()
    {
        var detail = Detail(usageBound: true, usage: []);

        var page = RenderUsageTab(detail);

        page.WaitForAssertion(
            () => Assert.Contains("No downstream consumers", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.DoesNotContain("Dependency usage is not bound", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void UsageTab_WhenUsageBoundWithConsumers_RendersBlastRadius()
    {
        var detail = Detail(usageBound: true,
            usage: [new ConsoleContentUsage("dash-1", "Q3 land use review", "dashboard", "high")]);

        var page = RenderUsageTab(detail);

        page.WaitForAssertion(
            () => Assert.Contains("Q3 land use review", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.DoesNotContain("Dependency usage is not bound", page.Markup, StringComparison.Ordinal);
    }

    private static IRenderedComponent<CatalogDetailPage> RenderUsageTab(ConsoleContentDetail detail)
    {
        var ctx = new Bunit.TestContext();
        ctx.Services.AddSingleton<IConsoleCatalogClient>(new StubCatalogClient(detail));
        ctx.Services.AddSingleton<IConsoleCatalogReadContextResolver>(new StubReadContextResolver());

        // The Usage tab is selected via the ?tab=usage query, which the page reads from the navigation URI.
        var nav = ctx.Services.GetRequiredService<Bunit.TestDoubles.FakeNavigationManager>();
        nav.NavigateTo($"/catalog/{detail.Summary.Id}?tab=usage");

        return ctx.RenderComponent<CatalogDetailPage>(parameters =>
            parameters.Add(p => p.IdOrSlug, detail.Summary.Id));
    }

    private static ConsoleContentDetail Detail(bool usageBound, ConsoleContentUsage[] usage) =>
        new()
        {
            Summary = new ConsoleContentSummary
            {
                Id = "storm-response-map",
                Slug = "storm-response-map",
                Type = "map",
                Title = "Storm response map",
                Summary = "Coastal storm response operations map.",
                Owner = "owner-1",
                Access = new ConsoleShareAccess { Sharing = CatalogSharingTiers.Public, Embeddable = true },
                ResolvedRole = "editor"
            },
            Description = "Coastal storm response operations map.",
            Capabilities = ["metadata-v2"],
            Usage = usage,
            UsageBound = usageBound
        };

    private sealed class StubReadContextResolver : IConsoleCatalogReadContextResolver
    {
        public Task<CatalogReadContext> ResolveAsync(string? publicLinkToken, CancellationToken cancellationToken = default) =>
            Task.FromResult(CatalogReadContext.Authenticated);
    }

    private sealed class StubCatalogClient(ConsoleContentDetail detail) : IConsoleCatalogClient
    {
        public Task<CatalogSearchResult> SearchAsync(CatalogListRequest request, CatalogReadContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CatalogSearchResult([detail.Summary], new Dictionary<string, int>(StringComparer.Ordinal), request));

        public Task<CatalogItemReadResult> GetCatalogItemAsync(string idOrSlug, CatalogReadContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(CatalogItemReadResult.Allowed(detail));

        public Task<CatalogItemReadResult> GetOpenDataItemAsync(string idOrSlug, CancellationToken cancellationToken = default) =>
            Task.FromResult(CatalogItemReadResult.Allowed(detail, anonymousRead: true));

        public Task<MapPackageReadResult> GetMapPackageAsync(string mapId, CatalogReadContext context, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<MapPackageReadResult> GetDraftMapAsync(string sourceItemId, CatalogReadContext context, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<MapPackageReadResult> AuthorizeEmbedAsync(string mapId, EmbedRouteOptions options, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
