using Bunit;
using Honua.Console.Contracts;
using Honua.Console.Shell.Layout;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

using static Honua.Console.IntegrationTests.ConsolePublicationRenderFixture;

namespace Honua.Console.IntegrationTests;

public sealed class ConsolePublicationScaleV4Tests
{
    [Fact]
    public void LegacyScaleLayoutPreservesPagingLazyLayersAndServerNameFiltering()
    {
        const ConsolePresentationMode presentation = ConsolePresentationMode.Legacy;
        var source = new PublicationNavigationSource();
        using var context = PublicationLayoutContext(source);
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = false });
        var header = context.Render<ConsoleTopNavigation>();
        var tree = context.Render<ConsolePublicationNavigation>();
        tree.WaitForAssertion(() => Assert.Equal(26, tree.FindAll("[role='treeitem']").Count));
        Assert.Equal(0, source.ChildReads);
        Assert.Equal(0, source.ConnectionReads);
        Assert.Equal("Filter services", tree.Find("label").TextContent);
        Assert.Null(tree.Find("input").GetAttribute("placeholder"));
        Assert.Contains("500 service names and 5000 layers", tree.Markup);
        Capture((tree.Markup, header.Markup), presentation, "initial");

        tree.Find("[data-tree-more]").Click();
        Assert.Equal(51, tree.FindAll("[role='treeitem']").Count);
        Capture((tree.Markup, header.Markup), presentation, "paged");
        tree.Find("[data-publication-key='service:service-000'] [data-tree-expand]").Click();
        tree.WaitForAssertion(() => Assert.Single(tree.FindAll("[aria-label='Bridges LineString']")));
        Assert.Equal(["service-000"], source.RequestedServices);
        Assert.Equal(1, source.ChildReads);
        Assert.Equal(1, source.ConnectionReads);
        Capture((tree.Markup, header.Markup), presentation, "expanded");

        tree.Find("input").Input("service-499");
        tree.WaitForAssertion(() => Assert.Contains("1 service name and 10 layers", tree.Markup));
        Assert.Equal(2, tree.FindAll("[role='treeitem']").Count);
        Assert.Equal("service-499", source.LastQuery.NameQuery);
        Assert.Equal(1, source.ChildReads);
        Capture((tree.Markup, header.Markup), presentation, "search");
        tree.Find("input").Input("absent-service");
        tree.WaitForAssertion(() => Assert.Contains("No services match this filter.", tree.Markup));
        Capture((tree.Markup, header.Markup), presentation, "empty");
        tree.Find("input").Input("");
        tree.WaitForAssertion(() => Assert.Contains("500 service names and 5000 layers", tree.Markup));
        Assert.Equal(1, source.ChildReads);
    }

    [Fact]
    public void V4TreeLoadsEveryServicePageAndShowsEachFolderWithItsServiceCount()
    {
        var source = new PublicationNavigationSource { ServiceSummaries = FolderedServices() };
        using var context = V4Context(source);
        var header = context.Render<ConsoleTopNavigation>();
        var tree = context.Render<ConsolePublicationNavigation>();
        tree.WaitForAssertion(() => Assert.Equal(41, tree.FindAll("[role='treeitem']").Count));
        Assert.Equal(6, source.ServiceReads);
        Assert.Equal(0, source.ChildReads);
        Assert.Empty(tree.FindAll("[data-tree-more]"));
        Assert.Equal("520", tree.Find("[data-publication-key='environment'] .console-tree-count").TextContent);
        var signals = tree.Find("[data-publication-key='folder:signals']");
        Assert.Equal("Signals", signals.QuerySelector("[data-tree-link]")!.TextContent);
        Assert.Equal("13", signals.QuerySelector(".console-tree-count")!.TextContent);
        Assert.Equal("folder", signals.QuerySelector(".console-tree-icon:not([data-icon='chevron'])")!.GetAttribute("data-icon"));
        Assert.Equal("Search in Services…", tree.Find("[data-tree-filter]").GetAttribute("placeholder"));
        Assert.Equal("Scope: Services", tree.Find(".console-tree-scope").TextContent);
        Assert.Equal("Live", tree.Find(".console-tree-live").TextContent);
        Assert.Contains("520 services", tree.Find(".console-tree-foot").TextContent);
        Assert.Single(header.FindAll("[data-tree-toggle]"));
        Assert.Empty(tree.FindAll("[data-tree-toggle]"));
        Capture((tree.Markup, header.Markup), ConsolePresentationMode.V4, "initial");

        tree.Find("[data-publication-key='folder:signals'] [data-tree-expand]").Click();
        Assert.Equal(54, tree.FindAll("[role='treeitem']").Count);
        Assert.Equal("folder-open", tree.Find("[data-publication-key='folder:signals'] .console-tree-icon:not([data-icon='chevron'])").GetAttribute("data-icon"));
        Capture((tree.Markup, header.Markup), ConsolePresentationMode.V4, "expanded");
        tree.Find("[data-tree-collapse-all]").Click();
        Assert.Equal(41, tree.FindAll("[role='treeitem']").Count);
    }

    [Fact]
    public void V4SearchGroupsFoldersServicesAndLayersAndOpensALayerInTheTree()
    {
        var source = new PublicationNavigationSource
        {
            ServiceSummaries = FolderedServices(),
            LayerMatches = [new(new("connection-one-layer-9", "signal-controllers", "Point"), "signals-00")]
        };
        using var context = V4Context(source);
        var header = context.Render<ConsoleTopNavigation>();
        var tree = context.Render<ConsolePublicationNavigation>();
        tree.WaitForAssertion(() => Assert.Equal(41, tree.FindAll("[role='treeitem']").Count));

        tree.Find("[data-tree-filter]").Input("signal");
        tree.WaitForAssertion(() => Assert.Equal(["Folders (1)", "Services (13)", "Layers (1)"],
            tree.FindAll("[data-publication-search] h3").Select(heading => heading.TextContent.Trim())));
        Assert.Equal(["signal"], source.LayerQueries);
        Assert.Equal(["connection-one"], source.LayerSearchConnections.Single());
        Assert.Equal("Folder", tree.Find("[data-publication-key='folder:signals'] .console-search-path").TextContent);
        Assert.Equal("Signals / signals-00 · 4 layers", tree.Find("[data-publication-key='service:signals-00'] .console-search-path").TextContent);
        Assert.Equal("point", tree.Find("[data-publication-key='search-layer:connection-one-layer-9'] .console-tree-icon").GetAttribute("data-icon"));
        Assert.Contains("15 matches in Services", tree.Find(".console-search-summary").TextContent);
        Capture((tree.Markup, header.Markup), ConsolePresentationMode.V4, "search");

        tree.Find("[data-publication-key='search-layer:connection-one-layer-9'] [data-tree-link]").Click();
        tree.WaitForAssertion(() => Assert.Equal("true",
            tree.Find("[data-publication-key='service:signals-00']").GetAttribute("aria-expanded")));
        Assert.True(string.IsNullOrEmpty(tree.Find("[data-tree-filter]").GetAttribute("value")));
        Assert.Equal(["signals-00"], source.RequestedServices);
        Assert.Empty(tree.FindAll("[data-publication-search]"));

        tree.Find("[data-tree-filter]").Input("absent-service");
        tree.WaitForAssertion(() => Assert.Contains("No services match this search.", tree.Markup));
        Assert.Contains("No Folders match.", tree.Markup);
        Capture((tree.Markup, header.Markup), ConsolePresentationMode.V4, "empty");
    }

    [Fact]
    public void V4LayerSearchFailureIsShownInItsGroupWithoutHidingServiceMatches()
    {
        var source = new PublicationNavigationSource
        {
            ServiceSummaries = FolderedServices(),
            LayerSearchResult = OperateSectionResult<IReadOnlyList<ConsolePublicationLayerMatch>>.Denied(
                OperateSectionStatus.Forbidden, "Layer search: Forbidden.")
        };
        using var context = V4Context(source);
        var tree = context.Render<ConsolePublicationNavigation>();
        tree.WaitForAssertion(() => Assert.Equal(41, tree.FindAll("[role='treeitem']").Count));
        tree.Find("[data-tree-filter]").Input("signals-01");
        tree.WaitForAssertion(() => Assert.Contains("Layer search: Forbidden.", tree.Markup));
        Assert.Single(tree.FindAll("[data-publication-key='service:signals-01']"));
        Assert.Equal("Layers", tree.Find("#console-search-layers").TextContent.Trim());
    }

    [Fact]
    public void V4RetriedSearchAlsoSearchesLayers()
    {
        var source = new PublicationNavigationSource
        {
            ServiceSummaries = FolderedServices(),
            LayerMatches = [new(new("connection-one-layer-9", "signal-controllers", "Point"), "signals-00")]
        };
        using var context = V4Context(source);
        var tree = context.Render<ConsolePublicationNavigation>();
        tree.WaitForAssertion(() => Assert.Equal(41, tree.FindAll("[role='treeitem']").Count));
        source.FailNext = OperateSectionResult<HonuaAdminServicePage>.Denied(OperateSectionStatus.Unavailable, "Catalog unavailable");
        tree.Find("[data-tree-filter]").Input("signal");
        tree.WaitForAssertion(() => Assert.Single(tree.FindAll("[data-tree-retry]")));
        Assert.Empty(source.LayerQueries);
        tree.Find("[data-tree-retry]").Click();
        tree.WaitForAssertion(() => Assert.Equal("Layers (1)", tree.Find("#console-search-layers").TextContent.Trim()));
        Assert.Equal(["signal"], source.LayerQueries);
    }

    [Fact]
    public void V4LateLayerResultsForAnOlderSearchAreDropped()
    {
        var source = new PublicationNavigationSource { ServiceSummaries = FolderedServices(), HoldLayerSearches = true };
        using var context = V4Context(source);
        var tree = context.Render<ConsolePublicationNavigation>();
        tree.WaitForAssertion(() => Assert.Equal(41, tree.FindAll("[role='treeitem']").Count));
        tree.Find("[data-tree-filter]").Input("sig");
        tree.WaitForAssertion(() => Assert.Single(source.HeldLayerSearches));
        tree.Find("[data-tree-filter]").Input("signal");
        tree.WaitForAssertion(() => Assert.Equal(2, source.HeldLayerSearches.Count));
        source.HeldLayerSearches[1].SetResult(LayerMatches("signal-controllers"));
        tree.WaitForAssertion(() => Assert.Equal("Layers (1)", tree.Find("#console-search-layers").TextContent.Trim()));
        source.HeldLayerSearches[0].SetResult(LayerMatches("sig-alpha", "sig-beta"));
        tree.Render();
        Assert.Equal("Layers (1)", tree.Find("#console-search-layers").TextContent.Trim());
        Assert.DoesNotContain("sig-alpha", tree.Markup);
    }

    [Fact]
    public void LegacyFilterKeepsTheOpenServiceVisible()
    {
        var source = new PublicationNavigationSource();
        using var context = PublicationLayoutContext(source);
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = false });
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("/operate/services/service-080/settings");
        var tree = context.Render<ConsolePublicationNavigation>();
        tree.WaitForAssertion(() => Assert.NotEmpty(tree.FindAll("[role='treeitem']")));
        tree.Find("input").Input("service");
        tree.WaitForAssertion(() => Assert.Equal("service:service-080",
            tree.Find("[aria-selected='true']").GetAttribute("data-publication-key")));
    }

    private static OperateSectionResult<IReadOnlyList<ConsolePublicationLayerMatch>> LayerMatches(params string[] names) =>
        OperateSectionResult<IReadOnlyList<ConsolePublicationLayerMatch>>.Allowed(
            names.Select((name, index) => new ConsolePublicationLayerMatch(new($"connection-one-layer-{index}", name, "Point"), "signals-00")).ToArray());

    internal static BunitContext V4Context(PublicationNavigationSource source)
    {
        var context = PublicationLayoutContext(source);
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = true });
        return context;
    }

    internal static Honua.Sdk.Admin.Models.ServiceSummary[] FolderedServices() =>
        Enumerable.Range(0, 39).Select(index => $"folder-{index:D2}").Append("signals")
            .SelectMany(folder => Enumerable.Range(0, 13).Select(index => new Honua.Sdk.Admin.Models.ServiceSummary
            {
                FolderId = folder,
                FolderName = folder == "signals" ? "Signals" : folder,
                ServiceName = $"{folder}-{index:D2}",
                LayerCount = 4
            })).ToArray();

    private static void Capture((string Tree, string Header) markup, ConsolePresentationMode presentation, string state)
    {
        var flag = presentation == ConsolePresentationMode.V4 ? "true" : "false";
        CatalogAdminPreview.Capture($"scale-{presentation.ToString().ToLowerInvariant()}-{state}",
            $"<div class=\"console-shell\" data-console-v4=\"{flag}\">{markup.Header}<div class=\"console-service-workspace\">{markup.Tree}<div class=\"console-workspace\"><main class=\"console-main\"><h1>Services</h1><p>500 services. Select a service to inspect its layers.</p></main></div></div></div>");
    }
}
