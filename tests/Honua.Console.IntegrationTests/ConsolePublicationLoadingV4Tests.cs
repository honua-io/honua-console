using Bunit;
using Honua.Console.Contracts;
using Honua.Console.Shell.Layout;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

using static Honua.Console.IntegrationTests.ConsolePublicationScaleV4Tests;

namespace Honua.Console.IntegrationTests;

public sealed class ConsolePublicationLoadingV4Tests
{
    [Fact]
    public void FailedBackgroundPageKeepsTheLoadedTreeAndTheFooterRetriesTheRest()
    {
        var source = new PublicationNavigationSource { ServiceSummaries = FolderedServices() };
        source.OnServiceRead = () =>
        {
            if (source.ServiceReads == 4)
                source.FailNext = OperateSectionResult<HonuaAdminServicePage>.Denied(OperateSectionStatus.Unavailable, "Page 4 timed out.");
        };
        using var context = V4Context(source);
        var tree = context.Render<ConsolePublicationNavigation>();
        tree.WaitForAssertion(() => Assert.Single(tree.FindAll("[data-tree-page-retry]")));
        Assert.Equal(4, source.ServiceReads);
        Assert.NotEmpty(tree.FindAll("[data-publication-tree] [role='treeitem']"));
        Assert.Empty(tree.FindAll("[data-tree-retry]"));
        Assert.Equal("Incomplete", tree.Find(".console-tree-live").TextContent);
        Assert.Contains("300 of 520 services", tree.Find(".console-tree-foot").TextContent);
        Assert.Equal("Page 4 timed out.", tree.Find(".console-tree-foot [role='status']").TextContent);

        tree.Find("[data-tree-page-retry]").Click();
        tree.WaitForAssertion(() => Assert.Equal("Live", tree.Find(".console-tree-live").TextContent));
        Assert.Equal(7, source.ServiceReads);
        Assert.Equal(41, tree.FindAll("[role='treeitem']").Count);
        Assert.Equal("13", tree.Find("[data-publication-key='folder:signals'] .console-tree-count").TextContent);
        Assert.Empty(tree.FindAll("[data-tree-page-retry]"));
    }

    [Fact]
    public void OpeningAResultDuringASlowCatalogRevealsItWithoutWaitingForLaterPages()
    {
        var source = new PublicationNavigationSource
        {
            ServiceSummaries = FolderedServices(),
            LayerMatches = [new(new("connection-one-layer-9", "road-centerlines", "LineString"), "folder-00-00")]
        };
        source.OnServiceRead = () => { if (source.ServiceReads is 2 or 5) source.HoldCount = 1; };
        using var context = V4Context(source);
        var tree = context.Render<ConsolePublicationNavigation>();
        tree.WaitForAssertion(() => Assert.Equal(2, source.ServiceReads));
        tree.Find("[data-tree-filter]").Input("folder-00");
        tree.WaitForAssertion(() => Assert.Equal("Layers (1)", tree.Find("#console-search-layers").TextContent.Trim()));
        Assert.Equal("folder-00", source.LastQuery.NameQuery);

        tree.Find("[data-publication-key='search-layer:connection-one-layer-9'] [data-tree-link]").Click();
        tree.WaitForAssertion(() => Assert.Equal("true",
            tree.Find("[data-publication-key='service:folder-00-00']").GetAttribute("aria-expanded")));
        Assert.Equal(5, source.ServiceReads);
        Assert.Equal(["folder-00-00"], source.RequestedServices);
        Assert.Empty(tree.FindAll("[data-publication-search]"));
    }

    [Fact]
    public void NavigatingDuringASearchKeepsTheSearchAndReadsNoCatalog()
    {
        var source = new PublicationNavigationSource { ServiceSummaries = FolderedServices() };
        using var context = V4Context(source);
        var tree = context.Render<ConsolePublicationNavigation>();
        tree.WaitForAssertion(() => Assert.Equal(41, tree.FindAll("[role='treeitem']").Count));
        tree.Find("[data-tree-filter]").Input("signal");
        tree.WaitForAssertion(() => Assert.Equal("Services (13)", tree.Find("#console-search-services").TextContent.Trim()));

        context.Services.GetRequiredService<NavigationManager>().NavigateTo("/operate/services/folder-00-00/settings");
        tree.WaitForAssertion(() => Assert.Equal("1", tree.Find("[data-publication-navigation]").GetAttribute("data-tree-revision")));
        Assert.Single(tree.FindAll("[data-publication-search]"));
        Assert.Equal("signal", tree.Find("[data-tree-filter]").GetAttribute("value"));
        Assert.Equal("Services (13)", tree.Find("#console-search-services").TextContent.Trim());

        tree.Find("[data-tree-filter]").Input("");
        tree.WaitForAssertion(() => Assert.Equal("true",
            tree.Find("[data-publication-key='service:folder-00-00']").GetAttribute("aria-selected")));
        Assert.Equal(6, source.ServiceReads);
    }

    [Theory]
    [InlineData(ConsolePresentationMode.V4, 1)]
    [InlineData(ConsolePresentationMode.Legacy, 0)]
    public void EnvironmentNameUnderV4HasItsOwnElementInsideTheConversationHeaderTarget(ConsolePresentationMode mode, int nameElements)
    {
        using var context = ConsolePublicationRenderFixture.PublicationLayoutContext(new PublicationNavigationSource());
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = mode == ConsolePresentationMode.V4 });
        var header = context.Render<ConsoleTopNavigation>();
        var target = header.Find("[data-workspace-header-target]");
        Assert.Equal(nameElements, target.QuerySelectorAll("[data-environment-name]").Length);
        Assert.Equal("Choose an environment", target.TextContent.Trim());
    }
}
