using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class CatalogAdminSettingsTests
{
    [Fact]
    public void Settings_ShowsScopeAndGovernanceWithoutOfferingAnUnsupportedWrite()
    {
        using var context = Context(Settings());
        var page = context.Render<CatalogAdminPage>(parameters => parameters
            .Add(p => p.CatalogId, "open").Add(p => p.Section, "settings"));
        Assert.Equal("Settings and scope", page.Find("nav[aria-label='Catalog sections'] [aria-current='page']").TextContent);
        Assert.Contains("Public", page.Find(".catalog-settings").TextContent);
        Assert.Contains("flood", page.Find(".catalog-settings").TextContent);
        Assert.Contains("after review", page.Find(".catalog-settings").TextContent);
        Assert.Contains("proposal", page.Find(".catalog-settings").TextContent, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(page.FindAll("form, button[type='submit']"));
        Assert.Contains("not supported", page.Find(".catalog-settings").TextContent);
        CatalogAdminPreview.Capture("settings", page.Markup);
    }

    [Fact]
    public void Settings_NeverInventsAnEmbedBundleOrExposesUnrelatedCatalogConfiguration()
    {
        using var context = Context(Settings() with { CatalogId = "staff", Title = "Private title" });
        var page = context.Render<CatalogAdminPage>(parameters => parameters
            .Add(p => p.CatalogId, "open").Add(p => p.Section, "settings"));
        Assert.DoesNotContain("Private title", page.Markup);
        Assert.Empty(page.FindAll("textarea, script"));
    }

    [Theory]
    [InlineData("https://catalog.example/components/v1.js", "sha384-AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", true)]
    [InlineData("javascript:alert(1)", "sha384-AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", false)]
    [InlineData("https://user:pass@catalog.example/components.js", "sha384-AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", false)]
    [InlineData("https://catalog.example/components.js?token=secret", "sha384-AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", false)]
    [InlineData("https://catalog.example/components.js#secret", "sha384-AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", false)]
    [InlineData("https://catalog.example/components.js", "", false)]
    public void Embed_RequiresAnApprovedExternalBundle(string href, string integrity, bool expected)
    {
        using var context = Context(Settings() with { Embed = new(href, integrity) });
        var page = context.Render<CatalogAdminPage>(parameters => parameters
            .Add(p => p.CatalogId, "open").Add(p => p.Section, "settings"));
        Assert.Equal(expected ? 1 : 0, page.FindAll("textarea[readonly]").Count);
        Assert.Empty(page.FindAll("script"));
        if (expected)
        {
            var snippet = page.Find("textarea").TextContent;
            Assert.Contains("type=\"module\"", snippet);
            Assert.Contains("catalog=\"open\"", snippet);
            Assert.DoesNotContain("apiKey", snippet);
        }
    }

    [Fact]
    public void Disabled_DoesNotReadOrRenderConfiguration()
    {
        using var context = Context(Settings());
        context.Services.AddSingleton(new CatalogAdminOptions());
        var page = context.Render<CatalogAdminPage>(parameters => parameters
            .Add(p => p.CatalogId, "open").Add(p => p.Section, "settings"));
        Assert.Empty(page.FindAll(".catalog-settings"));
        Assert.Contains("not enabled", page.Markup);
    }

    private static CatalogAdminSettings Settings() => new("open", "Open", "public")
    {
        FolderIds = ["safety"],
        Tags = ["flood"],
        Kinds = ["service", "app"],
        DefaultListing = "after-review",
        AllowedEmbedOrigins = ["https://county.example"],
        HostName = "catalog.example"
    };

    private static BunitContext Context(CatalogAdminSettings settings)
    {
        var context = new BunitContext();
        context.Services.AddSingleton(new CatalogAdminOptions { Enabled = true });
        context.Services.AddSingleton<ICatalogAdminSource>(new SettingsSource(settings));
        return context;
    }

    private sealed class SettingsSource(CatalogAdminSettings settings) : ICatalogAdminSource
    {
        public Task<CatalogAdminRead> ReadAsync(string? catalogId, CancellationToken cancellationToken) =>
            Task.FromResult(new CatalogAdminRead([], new("open", "Open", new(1, 0, 0, 0, 1), []), "fixture", null)
            { Settings = settings });
    }
}
