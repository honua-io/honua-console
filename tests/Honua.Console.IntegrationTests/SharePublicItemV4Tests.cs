using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class SharePublicItemV4Tests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PublicProjectionPreservesFormatsRoutesAndOwner(bool enabled)
    {
        using var context = Context(new ConsoleV4Options { Enabled = enabled }, new InMemoryConsoleCatalogClient());
        var page = context.Render<SharePublicItemPage>(parameters => parameters.Add(p => p.IdOrSlug, "coastal-flood-service"));
        Assert.Equal("Coastal Flood Service", page.Find("h1").TextContent);
        Assert.Equal(enabled ? 1 : 0, page.FindAll(".console-page-heading-v4").Count);
        Assert.Equal(enabled ? ["GeoServices feature service", "OGC API Features"] : ["GeoServices:FeatureService", "OGC:API:Features"], page.FindAll(".share-opendata-download-name").Select(node => node.TextContent));
        Assert.Equal("/share/public/items/coastal-flood-service", page.Find("[data-share-opendata-routes] code").TextContent);
        Assert.Contains("resilience-team", page.Find("[data-share-opendata-footer]").TextContent);
        Assert.Empty(page.FindAll("button,input,select,textarea"));
        Assert.Empty(page.FindAll(".console-panel .console-panel"));
        if (enabled)
        {
            Assert.Equal("Open data · Services", page.Find(".console-page-heading-v4 p").TextContent);
            Assert.Equal(["#flood", "#open-data", "#coastal"], page.FindAll(".console-chip-row span").Select(node => node.TextContent));
            Assert.Equal(["Download", "Routes"], page.FindAll("h2").Select(node => node.TextContent));
            Assert.Equal("/share/public", page.Find(".share-opendata-back").GetAttribute("href"));
        }
        CatalogAdminPreview.Capture($"share-public-{(enabled ? "v4" : "legacy")}", $"<div class=\"console-shell\" data-console-v4=\"{enabled.ToString().ToLowerInvariant()}\">{page.Markup}</div>");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnknownDownloadFormatIsPreserved(bool enabled)
    {
        var catalog = new InMemoryConsoleCatalogClient();
        var item = await catalog.GetOpenDataItemAsync("coastal-flood-service");
        item.Item!.Summary.Formats[0] = "custom-format/vNext";
        using var context = Context(new ConsoleV4Options { Enabled = enabled }, catalog);
        var page = context.Render<SharePublicItemPage>(parameters => parameters.Add(p => p.IdOrSlug, "coastal-flood-service"));
        Assert.Equal("custom-format/vNext", page.Find(".share-opendata-download-name").TextContent);
    }

    [Theory]
    [InlineData(false, "GeoServices:MapServer")]
    [InlineData(true, "GeoServices map service")]
    public async Task MapServiceDownloadLabelFollowsFlag(bool enabled, string expected)
    {
        var catalog = new InMemoryConsoleCatalogClient();
        var item = await catalog.GetOpenDataItemAsync("coastal-flood-service");
        item.Item!.Summary.Formats[0] = "GeoServices:MapServer";
        using var context = Context(new ConsoleV4Options { Enabled = enabled }, catalog);
        var page = context.Render<SharePublicItemPage>(parameters => parameters.Add(p => p.IdOrSlug, "coastal-flood-service"));
        Assert.Equal(expected, page.Find(".share-opendata-download-name").TextContent);
    }

    [Fact]
    public void PublicBackLinkStaysInsideApplicationMount()
    {
        using var context = Context(new ConsoleV4Options { Enabled = true }, new InMemoryConsoleCatalogClient());
        context.Services.AddSingleton<NavigationManager>(new MountedNavigationManager("/console/"));
        var page = context.Render<SharePublicItemPage>(parameters => parameters.Add(p => p.IdOrSlug, "coastal-flood-service"));
        Assert.Equal("/console/share/public", page.Find(".share-opendata-back").GetAttribute("href"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NonPublicItemNeverRendersItsDetails(bool enabled)
    {
        using var context = Context(new ConsoleV4Options { Enabled = enabled }, new InMemoryConsoleCatalogClient());
        var page = context.Render<SharePublicItemPage>(parameters => parameters.Add(p => p.IdOrSlug, "map-storm-response"));
        Assert.Contains("Public item not found", page.Markup);
        Assert.DoesNotContain("Storm Response Map", page.Markup);
        Assert.Empty(page.FindAll("[data-share-opendata-landing]"));
        Assert.Equal("/share/public", page.Find("a").GetAttribute("href"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingBindingStaysDistinctFromNotFound(bool enabled)
    {
        using var context = Context(new ConsoleV4Options { Enabled = enabled }, new UnsupportedConsoleCatalogClient());
        var page = context.Render<SharePublicItemPage>(parameters => parameters.Add(p => p.IdOrSlug, "coastal-flood-service"));
        Assert.Contains("Open data is not bound to honua-server", page.Markup);
        Assert.DoesNotContain("Public item not found", page.Markup);
        Assert.Empty(page.FindAll("[data-share-opendata-landing]"));
    }

    private static BunitContext Context(ConsoleV4Options options, IConsoleCatalogClient catalog)
    {
        var context = new BunitContext();
        context.Services.AddSingleton(options);
        context.Services.AddSingleton(catalog);
        context.Services.AddSingleton(ConsoleCapabilityTestManifest.All);
        return context;
    }
}
