using Bunit;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.Layout;
using Honua.Console.Shell.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

using static Honua.Console.IntegrationTests.ConsolePublicationRenderFixture;

namespace Honua.Console.IntegrationTests;

public sealed class ConsolePublicationSearchV4Tests
{
    [Fact]
    public void GroupedSearchPagesServicesWithoutReadingLayersAndClearsBackToTree()
    {
        var source = new PublicationNavigationSource();
        using var context = PublicationLayoutContext(source);
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = true });
        var tree = context.Render<ConsolePublicationNavigation>();
        tree.WaitForAssertion(() => Assert.Equal(26, tree.FindAll("[role='treeitem']").Count));
        tree.Find("input").Input(" service ");
        tree.WaitForAssertion(() => Assert.Single(tree.FindAll("[data-publication-search]")));
        Assert.Equal(["Folders", "Services (500)", "Layers", "Styles"],
            tree.FindAll("[data-publication-search] h3").Select(element => element.TextContent));
        Assert.Equal(25, tree.FindAll("[role='treeitem']").Count);
        Assert.Equal("service", source.LastQuery.NameQuery);
        Assert.NotEmpty(tree.FindAll("mark"));
        for (var page = 0; page < 4; page++)
        {
            tree.Find("[data-search-more]").Click();
            var expected = (page + 2) * 25;
            tree.WaitForAssertion(() => Assert.Equal(expected, tree.FindAll("[role='treeitem']").Count));
        }
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/operate/services/service-080/settings");
        tree.WaitForAssertion(() => Assert.Equal(125, tree.FindAll("[role='treeitem']").Count));
        Assert.Equal("service:service-080", tree.Find("[aria-selected='true']").GetAttribute("data-publication-key"));
        Assert.Equal(0, source.ChildReads);
        Assert.Equal(0, source.ConnectionReads);
        Assert.All(tree.FindAll("[data-tree-link]"), link => Assert.StartsWith("/operate/services/", link.GetAttribute("href")));
        tree.Find("input").Input("absent");
        tree.WaitForAssertion(() => Assert.Contains("No services match this search.", tree.Markup));
        Assert.Empty(tree.FindAll("[data-search-more]"));
        tree.Find("input").Input("");
        tree.WaitForAssertion(() => Assert.Empty(tree.FindAll("[data-publication-search]")));
        Assert.Equal(26, tree.FindAll("[role='treeitem']").Count);
    }

    [Fact]
    public void SearchMatchesPublicNameWhenTheTitleAndServiceNameDiffer()
    {
        var source = new PublicationNavigationSource
        {
            ServiceSummaries = [new() { ServiceName = "inventory", Title = "Bridges", PublicName = "roads-public" }]
        };
        using var context = PublicationLayoutContext(source);
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = true });
        var tree = context.Render<ConsolePublicationNavigation>();
        tree.WaitForAssertion(() => Assert.Equal(2, tree.FindAll("[role='treeitem']").Count));
        tree.Find("input").Input(" roads ");
        tree.WaitForAssertion(() => Assert.Single(tree.FindAll("[data-publication-search] [role='treeitem']")));
        Assert.Contains("Bridges", tree.Find("[data-publication-search] [role='treeitem']").TextContent);
        Assert.Equal("roads", source.LastQuery.NameQuery);
        Assert.Equal(" roads ", tree.Find("input").GetAttribute("value"));
    }

    [Fact]
    public void PendingSearchHidesServicesThatDoNotMatchTheNewQuery()
    {
        var source = new PublicationNavigationSource();
        using var context = PublicationLayoutContext(source);
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = true });
        var tree = context.Render<ConsolePublicationNavigation>();
        tree.WaitForAssertion(() => Assert.Equal(26, tree.FindAll("[role='treeitem']").Count));
        source.HoldCount = 1;
        tree.Find("input").Input("absent");
        tree.WaitForAssertion(() => Assert.Equal(2, source.ServiceReads));
        Assert.Empty(tree.FindAll("[role='treeitem']"));
        Assert.Equal("Services", tree.Find("#console-search-services").TextContent.Trim());
        Assert.Contains("Searching…", tree.Markup);
        Assert.DoesNotContain("No services match this search.", tree.Markup);
        Assert.Empty(tree.FindAll("[data-search-more], .console-search-summary"));
        source.ReleaseNext();
        tree.WaitForAssertion(() => Assert.Contains("Services <span>(0)</span>", tree.Markup));
    }

    [Fact]
    public void SearchReadsTheNextPageWhenLoadedServicesDoNotAllMatch()
    {
        var source = new PublicationNavigationSource
        {
            IgnoreNameQuery = true,
            ServiceSummaries = Enumerable.Range(0, 500)
                .Select(index => new Honua.Sdk.Admin.Models.ServiceSummary
                {
                    ServiceName = $"{(index % 4 == 0 ? "wanted" : "other")}-{index:D3}"
                }).ToArray()
        };
        using var context = PublicationLayoutContext(source);
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = true });
        var tree = context.Render<ConsolePublicationNavigation>();
        tree.WaitForAssertion(() => Assert.Equal(26, tree.FindAll("[role='treeitem']").Count));
        tree.Find("input").Input("wanted");
        tree.WaitForAssertion(() => Assert.Equal(25, tree.FindAll("[data-publication-search] [role='treeitem']").Count));
        Assert.Equal(2, source.ServiceReads);
        tree.Find("[data-search-more]").Click();
        tree.WaitForAssertion(() => Assert.Equal(3, source.ServiceReads));
        tree.WaitForAssertion(() => Assert.Equal(50, tree.FindAll("[data-publication-search] [role='treeitem']").Count));
        Assert.Equal(2, source.LastQuery.Page);
        Assert.Equal("wanted", source.LastQuery.NameQuery);
    }

    [Fact]
    public void SearchStopsPagingOnceEveryMatchIsVisible()
    {
        var source = new PublicationNavigationSource
        {
            IgnoreNameQuery = true,
            ServiceSummaries = Enumerable.Range(0, 30)
                .Select(index => new Honua.Sdk.Admin.Models.ServiceSummary
                {
                    ServiceName = $"{(index % 4 == 0 ? "wanted" : "other")}-{index:D3}"
                }).ToArray()
        };
        using var context = PublicationLayoutContext(source);
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = true });
        var tree = context.Render<ConsolePublicationNavigation>();
        tree.WaitForAssertion(() => Assert.NotEmpty(tree.FindAll("[role='treeitem']")));
        tree.Find("input").Input("wanted");
        tree.WaitForAssertion(() => Assert.Equal(8, tree.FindAll("[data-publication-search] [role='treeitem']").Count));
        Assert.Empty(tree.FindAll("[data-search-more]"));
        Assert.StartsWith("8 of 8 service matches", tree.Find(".console-search-summary").TextContent);
    }

    [Fact]
    public void HighlightEncodesUntrustedNamesAndMatchesWithoutCaseSensitivity()
    {
        using var context = new BunitContext();
        var highlight = context.Render<ConsoleSearchHighlight>(parameters => parameters
            .Add(component => component.Text, "<script>SERVICE & service</script>")
            .Add(component => component.Query, " service "));
        Assert.Equal(["SERVICE", "service"], highlight.FindAll("mark").Select(element => element.TextContent));
        Assert.Empty(highlight.FindAll("script"));
        Assert.Contains("&lt;script&gt;", highlight.Markup);
    }
}
