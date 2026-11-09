using AngleSharp.Html.Parser;
using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class CatalogAdminSettingsTests
{
    private const string Integrity = "sha384-AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    [Fact]
    public void Settings_ShowsScopeWithoutOfferingAnUnsupportedWrite()
    {
        using var context = Context(Settings());
        var page = context.Render<CatalogSettingsPage>(parameters => parameters.Add(p => p.CatalogId, "open"));
        Assert.Equal("Settings and scope", page.Find("nav[aria-label='Catalog sections'] [aria-current='page']").TextContent);
        Assert.Contains("Public", page.Find(".catalog-settings").TextContent);
        Assert.Contains("flood", page.Find(".catalog-settings").TextContent);
        Assert.Contains("after review", page.Find(".catalog-settings").TextContent);
        Assert.Empty(page.FindAll("form, button[type='submit']"));
        Assert.Contains("not supported", page.Find(".catalog-settings").TextContent);
        Assert.Single(page.FindAll("textarea[readonly]"));
        CatalogAdminPreview.Capture("settings", page.Markup);
    }

    [Fact]
    public void Settings_DoesNotExposeUnrelatedCatalogConfiguration()
    {
        using var context = Context(Settings() with { CatalogId = "staff", Title = "Private title" }, "open");
        var page = context.Render<CatalogSettingsPage>(parameters => parameters.Add(p => p.CatalogId, "open"));
        Assert.Contains("Catalog settings are unavailable", page.Markup);
        Assert.DoesNotContain("Private title", page.Markup);
        Assert.Empty(page.FindAll("textarea, script"));
    }

    [Fact]
    public void Settings_DoesNotInventAMissingEmbedBundle()
    {
        using var context = Context(Settings() with { Embed = null });
        var page = context.Render<CatalogSettingsPage>(parameters => parameters.Add(p => p.CatalogId, "open"));
        Assert.Empty(page.FindAll("textarea"));
        Assert.Contains("No released component bundle was reported", page.Markup);
    }

    [Theory]
    [InlineData("javascript:alert(1)", Integrity)]
    [InlineData("http://catalog.example/components.js", Integrity)]
    [InlineData("https://user:pass@catalog.example/components.js", Integrity)]
    [InlineData("https://catalog.example/components.js?token=secret", Integrity)]
    [InlineData("https://catalog.example/components.js#secret", Integrity)]
    [InlineData("https://catalog.example/components.js", "")]
    [InlineData("https://catalog.example/components.js", "sha256-AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("https://catalog.example/components.js", "sha384-AAAA")]
    [InlineData("https://catalog.example/components.js", Integrity + "\n")]
    [InlineData("https://catalog.example/components.js", null)]
    public void Embed_RejectsUnsafeOrUnidentifiedBundles(string href, string? integrity)
    {
        using var context = Context(Settings() with { Embed = new(href, integrity) });
        var page = context.Render<CatalogSettingsPage>(parameters => parameters.Add(p => p.CatalogId, "open"));
        Assert.Empty(page.FindAll("textarea, script"));
    }

    [Fact]
    public void Embed_EscapesCopiedMarkupAndRoundTripsTheOriginalCatalogIdentity()
    {
        const string catalogId = "a\"b<c&d</textarea>";
        const string encodedId = "a&quot;b&lt;c&amp;d&lt;/textarea&gt;";
        using var context = Context(Settings() with { CatalogId = catalogId });
        var page = context.Render<CatalogSettingsPage>(parameters => parameters.Add(p => p.CatalogId, catalogId));
        var snippet = page.Find("textarea").TextContent;
        Assert.Equal($"<script type=\"module\" src=\"https://catalog.example/components/v1.js\" integrity=\"{Integrity}\" crossorigin=\"anonymous\"></script>\n"
            + $"<honua-catalog-search catalog=\"{encodedId}\"></honua-catalog-search>", snippet);
        var document = new HtmlParser().ParseDocument(snippet);
        Assert.Equal(catalogId, document.QuerySelector("honua-catalog-search")!.GetAttribute("catalog"));
        Assert.Single(document.QuerySelectorAll("script[src]"));
        Assert.Single(document.QuerySelectorAll("honua-catalog-search"));
        Assert.Single(document.QuerySelector("honua-catalog-search")!.Attributes);
        Assert.Empty(page.FindAll("script"));
    }

    [Fact]
    public void SettingsRoute_ForwardsIdentityAndTabsPreserveEncodedDestinations()
    {
        const string catalogId = "parks & roads";
        using var context = Context(Settings() with { CatalogId = catalogId });
        Assert.Equal("/catalogs/{CatalogId}/settings", typeof(CatalogSettingsPage)
            .GetCustomAttributes(typeof(RouteAttribute), false).Cast<RouteAttribute>().Single().Template);
        var settings = context.Render<CatalogSettingsPage>(parameters => parameters.Add(p => p.CatalogId, catalogId));
        var tabs = settings.FindAll("nav[aria-label='Catalog sections'] a");
        Assert.Equal("/catalogs/parks%20%26%20roads", tabs[0].GetAttribute("href"));
        Assert.Equal("/catalogs/parks%20%26%20roads/settings", tabs[1].GetAttribute("href"));
        Assert.Equal("/catalogs/parks%20%26%20roads/categories", tabs[2].GetAttribute("href"));
        Assert.Equal("/catalogs/parks%20%26%20roads/templates", tabs[3].GetAttribute("href"));
        Assert.Null(tabs[0].GetAttribute("aria-current"));
        Assert.Equal("page", tabs[1].GetAttribute("aria-current"));
        var overview = context.Render<CatalogAdminPage>(parameters => parameters.Add(p => p.CatalogId, catalogId));
        Assert.Equal("Overview", overview.Find("nav[aria-label='Catalog sections'] [aria-current='page']").TextContent);
    }

    [Fact]
    public void Disabled_DoesNotReadOrRenderConfiguration()
    {
        using var context = Context(Settings());
        context.Services.AddSingleton(new CatalogAdminOptions());
        var page = context.Render<CatalogSettingsPage>(parameters => parameters.Add(p => p.CatalogId, "open"));
        Assert.Equal(0, ((SettingsSource)context.Services.GetRequiredService<ICatalogAdminSource>()).Reads);
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
        HostName = "catalog.example",
        Embed = new("https://catalog.example/components/v1.js", Integrity)
    };

    private static BunitContext Context(CatalogAdminSettings settings, string? overviewId = null)
    {
        var context = new BunitContext();
        context.Services.AddSingleton(new CatalogAdminOptions { Enabled = true });
        context.Services.AddSingleton<ICatalogAdminSource>(new SettingsSource(settings, overviewId));
        return context;
    }

    private sealed class SettingsSource(CatalogAdminSettings settings, string? overviewId) : ICatalogAdminSource
    {
        public int Reads { get; private set; }
        public Task<CatalogAdminRead> ReadAsync(CatalogAdminRequest request, CancellationToken cancellationToken)
        {
            Reads++;
            return Task.FromResult(new CatalogAdminRead([], new(overviewId ?? settings.CatalogId, "Overview title", new(1, 0, 0, 0, 1), []), "fixture", null)
            { Settings = settings });
        }
    }
}
