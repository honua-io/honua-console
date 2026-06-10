using Bunit;
using Honua.Console.Contracts;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Proves the server-bound catalog/content client renders Console catalog surfaces from live honua-server
/// data (issue #7 Definition of Done: Testcontainers coverage boots honua-server/PostgreSQL, seeds content
/// metadata, and asserts Catalog/Studio/Share/Operate-visible metadata renders from live server data;
/// Docker-unavailable environments skip cleanly). Drives <see cref="HonuaServerConsoleCatalogClient"/> over
/// the real <c>/api/v1/console/content</c> + <c>/api/v1/console/actions</c> contract (honua-server#1162) —
/// never an in-memory client.
/// </summary>
[Collection(ConsoleContentIntegrationCollection.Name)]
public sealed class CatalogServerBindingIntegrationTests
{
    private readonly ConsoleContentFixture _fixture;

    public CatalogServerBindingIntegrationTests(ConsoleContentFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task Catalog_SeedsContentAndRendersFromLiveServer()
    {
        Skip.If(_fixture.SkipReason is not null, _fixture.SkipReason ?? string.Empty);

        var client = _fixture.CreateContentClient();
        var catalog = new HonuaServerConsoleCatalogClient(client);

        // 1. Seed a known public content item on the live server through the real create path.
        var name = $"console-catalog-fixture-{Guid.NewGuid():N}";
        var title = $"Console catalog fixture {Guid.NewGuid():N}";
        var created = await client.CreateAsync(new HonuaCreateConsoleContentItemRequest
        {
            Name = name,
            ItemType = HonuaConsoleContentItemTypes.Service,
            Title = title,
            Description = "Seeded by the Console catalog Testcontainers integration test.",
            Visibility = HonuaConsoleVisibilities.Public,
            Tags = ["console-fixture", "open-data"]
        });

        // Past the environment/auth skip gate, a known-valid create must be accepted by the live server;
        // a rejection is a real regression, never a false-green skip.
        Assert.Null(created.Issue);
        Assert.NotNull(created.Data);
        var itemId = created.Data!.Id;
        Assert.False(string.IsNullOrWhiteSpace(itemId));
        // The server authors the RBAC verbs — the creating admin principal must at least be able to view.
        Assert.Contains(HonuaConsoleContentActions.View, created.Data.Actions, StringComparer.Ordinal);

        // 2. The catalog search surfaces the seeded item from live data (not a mock).
        var search = await catalog.SearchAsync(
            new CatalogListRequest { Query = title },
            CatalogReadContext.Authenticated);
        Assert.Contains(search.Items, summary => summary.Id == itemId && summary.Title == title);

        // 3. The detail read maps the live item through the server-bound mapper.
        var detail = await catalog.GetCatalogItemAsync(itemId, CatalogReadContext.Authenticated);
        Assert.Equal(CatalogReadStatus.Allowed, detail.Status);
        Assert.Equal(title, detail.Item!.Summary.Title);
        Assert.Contains("metadata-v2", detail.Item.Capabilities);

        // 4. The Catalog page renders the seeded item from the live server-bound client.
        using var ctx = new Bunit.BunitContext();
        ctx.Services.AddSingleton<IConsoleCatalogClient>(catalog);
        ctx.Services.AddSingleton<IConsoleCatalogReadContextResolver>(new AuthenticatedReadContextResolver());

        var page = ctx.Render<CatalogPage>();
        page.WaitForAssertion(
            () => Assert.Contains(title, page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(10));
    }

    private sealed class AuthenticatedReadContextResolver : IConsoleCatalogReadContextResolver
    {
        public Task<CatalogReadContext> ResolveAsync(string? publicLinkToken, CancellationToken cancellationToken = default) =>
            Task.FromResult(CatalogReadContext.Authenticated);
    }
}
