using Bunit;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class CatalogBrowseV4Tests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SearchKeepsFiltersTypesAndItemActions(bool enabled)
    {
        using var context = AuthenticatedContext(new ConsoleV4Options { Enabled = enabled });
        var page = context.Render<CatalogPage>();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("tbody tr")));
        Assert.Equal(enabled ? 1 : 0, page.FindAll(".console-page-heading-v4").Count);
        Assert.Equal(new[] { "q", "type", "tag", "owner", "visibility", "sort" },
            page.FindAll("form :is(input, select)").Select(field => field.GetAttribute("name")));
        Assert.NotEmpty(page.FindAll("nav.console-type-strip a"));
        Assert.NotEmpty(page.FindAll("tbody a[href^='/catalog/']"));
        Assert.Single(page.FindAll("a[href='/operate/publishing']"));
        if (enabled)
        {
            Assert.Equal(page.FindAll("tbody tr").Count.ToString(), page.Find(".console-metric-value").TextContent);
            Assert.Equal("Catalog content", page.Find(".console-panel-heading h2").TextContent);
            CatalogAdminPreview.CaptureV4("catalog-browse-v4", page.Markup);
        }
        else
        {
            Assert.Single(page.FindAll(".console-kicker"));
            Assert.Single(page.FindAll(".console-result-count"));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnonymousSearchRequiresSignInWithoutLeakingContent(bool enabled)
    {
        using var context = AnonymousContext(new ConsoleV4Options { Enabled = enabled });
        var page = context.Render<CatalogPage>();
        page.WaitForAssertion(() => Assert.Contains("Sign in to browse catalog", page.Markup));
        Assert.Empty(page.FindAll("form, table, .console-metric-tile"));
        Assert.Single(page.FindAll(".console-state-unauthenticated"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DetailKeepsOverviewAndNavigableTabs(bool enabled)
    {
        using var context = AuthenticatedContext(new ConsoleV4Options { Enabled = enabled });
        var page = context.Render<CatalogDetailPage>(p => p.Add(x => x.IdOrSlug, "map-storm-response"));
        page.WaitForAssertion(() => Assert.Contains("Storm Response Map", page.Find("h1").TextContent));
        Assert.Equal(enabled ? 1 : 0, page.FindAll(".console-page-heading-v4").Count);
        Assert.Equal(8, page.FindAll("nav.console-tab-row a").Count);
        Assert.Contains("Item ID", page.Find(".console-detail-panel").TextContent);
        Assert.NotEmpty(page.FindAll("a.console-button-link"));
        Assert.Single(page.FindAll("h2, h3").Where(heading => heading.TextContent == "Overview"));
        if (enabled)
        {
            Assert.Single(page.FindAll("nav a[aria-current='page']"));
            Assert.Empty(page.FindAll(".console-overview-card"));
            CatalogAdminPreview.CaptureV4("catalog-item-overview-v4", page.Markup);
        }
        else
        {
            Assert.NotEmpty(page.FindAll(".console-overview-card"));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PublicLinkKeepsReadActionsAndTokenWhileHidingPermissions(bool enabled)
    {
        using var context = AnonymousContext(new ConsoleV4Options { Enabled = enabled });
        context.Services.GetRequiredService<NavigationManager>()
            .NavigateTo("catalog/map-storm-response?token=pl-storm-map&tab=permissions");
        var page = context.Render<CatalogDetailPage>(p => p.Add(x => x.IdOrSlug, "map-storm-response"));
        page.WaitForAssertion(() => Assert.Contains("Permissions are hidden", page.Markup));
        Assert.All(page.FindAll("nav.console-tab-row a"), link => Assert.Contains("token=pl-storm-map", link.GetAttribute("href")));
        var actions = page.FindAll(".console-detail-actions a, .console-page-heading-actions a");
        Assert.Equal(new[] { "Open Detail", "Open Map" }, actions.Select(action => action.TextContent));
        Assert.All(actions, action => Assert.Contains("token=pl-storm-map", action.GetAttribute("href")));
        if (enabled) CatalogAdminPreview.CaptureV4("catalog-item-permissions-v4", page.Markup);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SearchAnnouncesResultCountAfterDelayedResponse(bool enabled)
    {
        using var context = AuthenticatedContext(new ConsoleV4Options { Enabled = enabled });
        var pending = new TaskCompletionSource<CatalogSearchResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Services.AddSingleton<IConsoleCatalogClient>(new PendingSearchClient(pending.Task));
        var page = context.Render<CatalogPage>();
        page.WaitForAssertion(() => Assert.Contains("Loading catalog", page.Markup));
        Assert.Single(page.FindAll("[role='status']"));
        if (enabled) Assert.Contains("Not available", page.Find("[role='status']").TextContent);

        var result = await new InMemoryConsoleCatalogClient().SearchAsync(new CatalogListRequest(), CatalogReadContext.Authenticated);
        await page.InvokeAsync(() => pending.SetResult(result));
        page.WaitForAssertion(() => Assert.Contains(result.Items.Count.ToString(), page.Find("[role='status']").TextContent));
        Assert.Contains("Items shown", page.Find("[role='status']").TextContent, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class PendingSearchClient(Task<CatalogSearchResult> search) : IConsoleCatalogClient
    {
        public Task<CatalogSearchResult> SearchAsync(CatalogListRequest request, CatalogReadContext context, CancellationToken cancellationToken = default) => search;
        public Task<CatalogItemReadResult> GetCatalogItemAsync(string idOrSlug, CatalogReadContext context, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CatalogItemReadResult> GetOpenDataItemAsync(string idOrSlug, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MapPackageReadResult> GetMapPackageAsync(string mapId, CatalogReadContext context, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MapPackageReadResult> GetDraftMapAsync(string sourceItemId, CatalogReadContext context, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MapPackageReadResult> AuthorizeEmbedAsync(string mapId, EmbedRouteOptions options, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private static BunitContext AuthenticatedContext(ConsoleV4Options options) =>
        CreateContext(options, new AuthenticatedReadContextResolver());

    private static BunitContext AnonymousContext(ConsoleV4Options options) =>
        CreateContext(options, new AnonymousReadContextResolver());

    private static BunitContext CreateContext(ConsoleV4Options options, IConsoleCatalogReadContextResolver resolver)
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton(options);
        context.Services.AddSingleton<IConsoleCatalogClient>(new InMemoryConsoleCatalogClient());
        context.Services.AddSingleton(resolver);
        context.Services.AddSingleton<IOperateTransitionDataSource>(new UnsupportedOperateTransitionDataSource());
        context.Services.AddSingleton(ConsoleCapabilityTestManifest.All);
        return context;
    }

    private sealed class AuthenticatedReadContextResolver : IConsoleCatalogReadContextResolver
    {
        public Task<CatalogReadContext> ResolveAsync(string? publicLinkToken, CancellationToken cancellationToken = default) =>
            Task.FromResult(CatalogReadContext.Authenticated);
    }

    private sealed class AnonymousReadContextResolver : IConsoleCatalogReadContextResolver
    {
        public Task<CatalogReadContext> ResolveAsync(string? publicLinkToken, CancellationToken cancellationToken = default) =>
            Task.FromResult(CatalogReadContext.AnonymousPublicLink(publicLinkToken));
    }
}
