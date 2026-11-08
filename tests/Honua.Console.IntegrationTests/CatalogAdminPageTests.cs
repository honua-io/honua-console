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

    private static BunitContext Context(ICatalogAdminSource source)
    {
        var context = new BunitContext();
        context.Services.AddSingleton(new CatalogAdminOptions { Enabled = true });
        context.Services.AddSingleton(source);
        return context;
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
