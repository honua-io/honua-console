using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class MapsMetadataAiV4Tests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MapViewerKeepsChipsActionsAndLayersUnderBothFlags(bool enabled)
    {
        using var context = CatalogContext(enabled, anonymous: false);
        var page = context.Render<MapViewerPage>(p => p.Add(x => x.MapId, "map-storm-response"));

        page.WaitForAssertion(() => Assert.Equal("Storm Response Map", page.Find("h1").TextContent));
        var package = await new InMemoryConsoleCatalogClient().GetMapPackageAsync("map-storm-response", CatalogReadContext.Authenticated);
        var expectedActions = MapViewerPage.ResolveViewerActions(package.MapPackage!.Summary, isAuthenticated: true)
            .Select(action => action.Label).ToArray();
        Assert.NotEmpty(expectedActions);
        Assert.Equal(enabled ? 1 : 0, page.FindAll(".console-page-heading-v4").Count);
        Assert.Equal(new[] { "Honua Streets", "Public link" }, page.FindAll(".console-chip-row span").Select(node => node.TextContent).Take(2));
        Assert.NotEmpty(page.FindAll(".console-map-panel li"));
        if (enabled)
        {
            Assert.Equal(expectedActions, page.FindAll(".console-page-heading-actions a.console-button-link").Select(node => node.TextContent));
            Assert.Equal("Layers", page.Find("section.console-panel.console-map-panel h2").TextContent);
            Assert.Equal("region", page.Find(".console-map-surface").GetAttribute("role"));
            Assert.Empty(page.FindAll(".console-kicker"));
            Assert.Empty(page.FindAll("aside"));
            CatalogAdminPreview.CaptureV4("map-viewer-v4", page.Markup);
        }
        else
        {
            Assert.Equal(expectedActions, page.FindAll(".console-detail-actions a.console-button-link").Select(node => node.TextContent));
            Assert.Equal("Map viewer", page.Find(".console-detail-heading .console-kicker").TextContent);
            Assert.Equal("Map layers", page.Find("aside.console-map-panel").GetAttribute("aria-label"));
            Assert.Null(page.Find(".console-map-surface").GetAttribute("role"));
            Assert.Equal("console-page console-viewer-page", page.Find("section").GetAttribute("class"));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnonymousPublicLinkViewerShowsTheMapWithoutActions(bool enabled)
    {
        using var context = CatalogContext(enabled, anonymous: true);
        context.Services.AddSingleton<NavigationManager>(new MountedNavigationManager("/", "maps/map-storm-response?token=pl-storm-map"));
        var page = context.Render<MapViewerPage>(p => p.Add(x => x.MapId, "map-storm-response"));

        page.WaitForAssertion(() => Assert.Equal("Storm Response Map", page.Find("h1").TextContent));
        Assert.NotEmpty(page.FindAll(".console-map-panel li"));
        Assert.Equal(3, page.FindAll(".console-chip-row span").Count);
        Assert.Empty(page.FindAll("a.console-button-link"));
        Assert.Empty(page.FindAll(".console-page-heading-actions"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MapDraftKeepsUnsavedCanvasAndLayersUnderBothFlags(bool enabled)
    {
        using var context = CatalogContext(enabled, anonymous: false);
        context.Services.AddSingleton<NavigationManager>(new MountedNavigationManager("/", "maps/new?from=coastal-flood-service"));
        var page = context.Render<MapDraftPage>();

        page.WaitForAssertion(() => Assert.Contains("Unsaved draft", page.Find(".console-map-canvas").TextContent));
        Assert.Equal(enabled ? 1 : 0, page.FindAll(".console-page-heading-v4").Count);
        Assert.Equal(enabled ? 0 : 1, page.FindAll(".console-kicker").Count);
        Assert.Equal(enabled ? 0 : 1, page.FindAll("aside.console-map-panel[aria-label='Draft map layers']").Count);
        Assert.NotEmpty(page.FindAll(".console-map-panel li"));
        if (enabled)
        {
            Assert.Equal("Layers", page.Find("section.console-panel.console-map-panel h2").TextContent);
            CatalogAdminPreview.CaptureV4("map-draft-v4", page.Markup);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MetadataPreviewKeepsCatalogLinkAndSectionsUnderBothFlags(bool enabled)
    {
        using var context = MetadataFixtureSource.Context();
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = enabled });
        var page = context.Render<MetadataPreviewPage>(p => p.Add(x => x.DatasetId, "flood"));

        Assert.Equal("Dataset metadata", page.Find("h1").TextContent);
        Assert.Equal(enabled ? 1 : 0, page.FindAll(".console-page-heading-v4").Count);
        Assert.Single(page.FindAll("a[href='/catalogs']"));
        Assert.NotEmpty(page.FindAll(".metadata-preview-nav a"));
        Assert.NotEmpty(page.FindAll("[data-override='frequency']"));
        if (enabled)
        {
            CatalogAdminPreview.CaptureV4("metadata-dataset-v4", page.Markup);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConversationEntryKeepsOneOpenButtonAndLinksUnderBothFlags(bool enabled)
    {
        using var context = new BunitContext();
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = enabled });
        var page = context.Render<OmniPromptConsolePage>();

        Assert.Equal("Continue your conversation", page.Find("h1").TextContent);
        Assert.Single(page.FindAll("[data-workspace-open]"));
        Assert.Equal(enabled ? 1 : 0, page.FindAll(".console-page-heading-actions [data-workspace-open]").Count);
        Assert.Equal(enabled ? 0 : 1, page.FindAll(".console-kicker").Count);
        Assert.Equal(4, page.FindAll(".console-action-row a").Count);
        if (enabled)
        {
            CatalogAdminPreview.CaptureV4("conversation-entry-v4", page.Markup);
        }
    }

    private static BunitContext CatalogContext(bool enabled, bool anonymous)
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = enabled });
        context.Services.AddSingleton<IConsoleCatalogClient>(new InMemoryConsoleCatalogClient());
        context.Services.AddSingleton<IConsoleCatalogReadContextResolver>(new StubReadContextResolver(anonymous));
        context.Services.AddSingleton(ConsoleCapabilityTestManifest.All);
        return context;
    }

    private sealed class StubReadContextResolver(bool anonymous) : IConsoleCatalogReadContextResolver
    {
        public Task<CatalogReadContext> ResolveAsync(string? publicLinkToken, CancellationToken cancellationToken = default) =>
            Task.FromResult(anonymous ? CatalogReadContext.AnonymousPublicLink(publicLinkToken) : CatalogReadContext.Authenticated);
    }
}
