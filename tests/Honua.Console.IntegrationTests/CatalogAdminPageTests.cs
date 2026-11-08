using Bunit;
using Honua.Console.Shell.DependencyInjection;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class CatalogAdminPageTests
{
    [Fact]
    public void FlagDisabled_DoesNotReadOrShowFixtureContent()
    {
        using var context = new BunitContext();
        var source = new FixtureCatalogAdminSource();
        context.Services.AddSingleton<ICatalogAdminSource>(source);
        context.Services.AddSingleton(new CatalogAdminOptions());
        var page = context.Render<CatalogAdminPage>();
        Assert.Contains("Catalogs are not enabled", page.Markup);
        Assert.DoesNotContain("Open data", page.Markup);
        Assert.Equal(0, source.Reads);
    }

    [Fact]
    public void List_UsesLabeledFiltersAndAudience_WithoutClaimingLive()
    {
        using var context = Context(new FixtureCatalogAdminSource());
        var page = context.Render<CatalogAdminPage>();
        page.WaitForAssertion(() => Assert.Contains("Open data", page.Markup));
        Assert.NotNull(page.Find("label[for='catalog-query']"));
        Assert.NotNull(page.Find("label[for='catalog-audience']"));
        Assert.Contains("Signed-in users", page.Markup);
        Assert.Contains("Fixture preview", page.Markup);
        Assert.DoesNotContain(">Live<", page.Markup);
        CatalogAdminPreview.Capture("list", page.Markup);
        page.Find("#catalog-audience").Change("signed-in");
        Assert.DoesNotContain("Open data", page.Find("tbody").TextContent);
        Assert.Contains("Staff", page.Find("tbody").TextContent);
    }

    [Fact]
    public void Overview_DistinguishesAdminCountsFromPublicResults()
    {
        using var context = Context(new FixtureCatalogAdminSource());
        var page = context.Render<CatalogAdminPage>(parameters => parameters.Add(p => p.CatalogId, "open-data"));
        page.WaitForAssertion(() => Assert.Contains("Audience-restricted", page.Markup));
        Assert.Contains("Admin visibility counts", page.Markup);
        Assert.Contains("Hidden here", page.Markup);
        Assert.Contains("Missing description", page.Markup);
        Assert.Contains("Not reported", page.Markup);
        Assert.DoesNotContain("Approve", page.Markup);
    }

    [Fact]
    public void ConfiguredServerWithoutContract_DoesNotFallBackToFixtures()
    {
        using var context = new BunitContext();
        context.Services.AddHonuaConsoleShell(new HonuaConsoleShellOptions
        {
            ServerBaseUrl = "https://server.example/",
            CatalogAdminEnabled = true
        });
        var page = context.Render<CatalogAdminPage>();
        page.WaitForAssertion(() => Assert.Contains("Catalog contract is not supported", page.Markup));
        Assert.DoesNotContain("Open data", page.Markup);
    }

    [Fact]
    public void MissingServer_RendersMissingBinding()
    {
        using var context = new BunitContext();
        context.Services.AddHonuaConsoleShell(new HonuaConsoleShellOptions { CatalogAdminEnabled = true });
        var page = context.Render<CatalogAdminPage>();
        page.WaitForAssertion(() => Assert.Contains("Configure a server", page.Markup));
        Assert.DoesNotContain("Open data", page.Markup);
    }

    [Fact]
    public void List_PagesAndResetsAfterSearch()
    {
        var rows = Enumerable.Range(1, 25).Select(index =>
            new CatalogAdminRow($"catalog-{index}", $"Catalog {index:00}", "public", "All kinds", null)).ToArray();
        using var context = Context(new CallbackCatalogAdminSource((_, _) =>
            Task.FromResult(new CatalogAdminRead(rows, null, "fixture", null))));
        var page = context.Render<CatalogAdminPage>();
        Assert.Equal(12, page.FindAll("tbody tr").Count);
        page.FindAll("nav button")[1].Click();
        Assert.Contains("Catalog 13", page.Find("tbody").TextContent);
        page.FindAll("nav button")[1].Click();
        Assert.Single(page.FindAll("tbody tr"));
        Assert.Contains("Catalog 25", page.Find("tbody").TextContent);
        Assert.True(page.FindAll("nav button")[1].HasAttribute("disabled"));
        page.Find("nav button").Click();
        Assert.Contains("Catalog 13", page.Find("tbody").TextContent);
        page.Find("#catalog-query").Input("25");
        Assert.Single(page.FindAll("tbody tr"));
        Assert.Contains("Catalog 25", page.Find("tbody").TextContent);
        Assert.True(page.Find("nav button").HasAttribute("disabled"));
    }

    [Fact]
    public void MismatchedOverview_DoesNotShowItsTitleOrCounts()
    {
        using var context = Context(new FixtureCatalogAdminSource());
        var page = context.Render<CatalogAdminPage>(parameters => parameters.Add(p => p.CatalogId, "other"));
        page.WaitForAssertion(() => Assert.Contains("Catalog is unavailable", page.Markup));
        Assert.DoesNotContain("Open data", page.Markup);
        Assert.DoesNotContain("Admin visibility counts", page.Markup);
    }

    [Fact]
    public void DelayedOverview_CannotReplaceANewerRoute()
    {
        var pending = new TaskCompletionSource<CatalogAdminRead>();
        CancellationToken firstRead = default;
        using var context = Context(new CallbackCatalogAdminSource((catalogId, cancellationToken) =>
        {
            if (catalogId == "old")
            {
                firstRead = cancellationToken;
                return pending.Task;
            }
            return Task.FromResult(new CatalogAdminRead([], null, "unavailable", "Current catalog is unavailable."));
        }));
        var page = context.Render<CatalogAdminPage>(parameters => parameters.Add(p => p.CatalogId, "old"));
        page.Render(parameters => parameters.Add(p => p.CatalogId, "new"));
        Assert.True(firstRead.IsCancellationRequested);
        var renderCount = page.RenderCount;
        pending.SetResult(new([], null, "unavailable", "Old private response"));
        page.WaitForState(() => page.RenderCount > renderCount);
        page.WaitForAssertion(() => Assert.Contains("Current catalog is unavailable", page.Markup));
        Assert.DoesNotContain("Old private response", page.Markup);
    }

    [Fact]
    public void FailedRead_DoesNotDisplayExceptionDetails()
    {
        using var context = Context(new CallbackCatalogAdminSource((_, _) =>
            Task.FromException<CatalogAdminRead>(new InvalidOperationException("Hidden response title"))));
        var page = context.Render<CatalogAdminPage>();
        page.WaitForAssertion(() => Assert.Contains("Refresh to try again", page.Markup));
        Assert.DoesNotContain("Hidden response title", page.Markup);
    }

    [Fact]
    public void List_SortSelectionMatchesRowsAndResetsPaging()
    {
        var rows = Enumerable.Range(1, 25).Reverse().Select(index =>
            new CatalogAdminRow($"catalog-{index:00}", "Same title", index == 1 ? "roles" : "public", "All kinds", null)).ToArray();
        using var context = Context(new CallbackCatalogAdminSource((_, _) =>
            Task.FromResult(new CatalogAdminRead(rows, null, "fixture", null))));
        var page = context.Render<CatalogAdminPage>();
        Assert.EndsWith("catalog-01", page.Find("tbody a").GetAttribute("href"));
        page.FindAll("nav button")[1].Click();
        page.Find("#catalog-sort").Change("true");
        Assert.Equal("true", page.Find("#catalog-sort").GetAttribute("value"));
        Assert.True(page.Find("nav button").HasAttribute("disabled"));
        Assert.EndsWith("catalog-01", page.Find("tbody a").GetAttribute("href"));
        page.Find("#catalog-sort").Change("false");
        Assert.Equal("false", page.Find("#catalog-sort").GetAttribute("value"));
        page.FindAll("nav button")[1].Click();
        page.Find("#catalog-audience").Change("roles");
        Assert.Single(page.FindAll("tbody tr"));
        Assert.True(page.Find("nav button").HasAttribute("disabled"));
    }

    [Fact]
    public void List_SortsShuffledTitlesInBothDirections()
    {
        CatalogAdminRow[] rows = [new("z", "Zebra", "public", "", null), new("a", "Alpha", "public", "", null)];
        using var context = Context(new CallbackCatalogAdminSource((_, _) =>
            Task.FromResult(new CatalogAdminRead(rows, null, "fixture", null))));
        var page = context.Render<CatalogAdminPage>();
        Assert.Equal("Alpha", page.Find("tbody a").TextContent);
        page.Find("#catalog-sort").Change("true");
        Assert.Equal("Zebra", page.Find("tbody a").TextContent);
        page.Find("#catalog-sort").Change("false");
        Assert.Equal("Alpha", page.Find("tbody a").TextContent);
    }

    [Theory]
    [InlineData("/inbox?proposalId=proposal-1", true)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("https://other.example/inbox?proposalId=proposal-1", false)]
    [InlineData("/inbox?proposalId=proposal-1#token", false)]
    [InlineData("/inbox?proposalId=bad\\path", false)]
    public void List_OnlyLinksAllowedProposalDestinations(string href, bool linked)
    {
        using var context = Context(new CallbackCatalogAdminSource((_, _) => Task.FromResult(
            new CatalogAdminRead([new("open", "Open", "public", "", href)], null, "fixture", null))));
        var page = context.Render<CatalogAdminPage>();
        Assert.Equal(linked ? 1 : 0, page.FindAll("tbody a[href^='/inbox?proposalId=']").Count);
        Assert.Empty(page.FindAll("a[href^='javascript:'], a[href^='https://other.example']"));
    }

    private static BunitContext Context(ICatalogAdminSource source)
    {
        var context = new BunitContext();
        context.Services.AddSingleton(new CatalogAdminOptions { Enabled = true });
        context.Services.AddSingleton(source);
        return context;
    }

    private sealed class CallbackCatalogAdminSource(Func<string?, CancellationToken, Task<CatalogAdminRead>> read) : ICatalogAdminSource
    {
        public Task<CatalogAdminRead> ReadAsync(string? catalogId, CancellationToken cancellationToken) => read(catalogId, cancellationToken);
    }

    private sealed class FixtureCatalogAdminSource : ICatalogAdminSource
    {
        public int Reads { get; private set; }

        public Task<CatalogAdminRead> ReadAsync(string? catalogId, CancellationToken cancellationToken)
        {
            Reads++;
            IReadOnlyList<CatalogAdminRow> rows =
            [
                new("open-data", "Open data", "public", "Folder: Public", null),
                new("staff", "Staff", "signed-in", "All kinds", null)
            ];
            var overview = catalogId is null ? null : new CatalogAdminOverview(
                "open-data", "Open data", new(6, 2, 1, 1, 10),
                [new("Flood zones", "Missing description")]);
            return Task.FromResult(new CatalogAdminRead(rows, overview, "fixture", null));
        }
    }
}
