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
    [Theory]
    [InlineData(201)]
    [InlineData(4800)]
    public void OverlongSearchExplainsTheLimitAndRecoversWhenEditedOrCleared(int length)
    {
        var source = new PublicationNavigationSource();
        using var context = PublicationLayoutContext(source);
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = true });
        var tree = context.Render<ConsolePublicationNavigation>();
        tree.WaitForAssertion(() => Assert.Equal(201, tree.FindAll("[role='treeitem']").Count));
        var query = new string('x', length);
        tree.Find("input").Input(query);
        tree.WaitForAssertion(() => Assert.Contains("200 characters", tree.Find("[role='alert']").TextContent));
        Assert.Equal(5, source.ServiceReads);
        Assert.Equal(query, tree.Find("input").GetAttribute("value"));
        Assert.Equal("true", tree.Find("input").GetAttribute("aria-invalid"));
        Assert.False(tree.Find("input").HasAttribute("disabled"));
        Assert.DoesNotContain("Temporarily unavailable", tree.Markup);
        tree.Find("input").Input(" service ");
        tree.WaitForAssertion(() => Assert.Equal("Services (500)", tree.Find("#console-search-services").TextContent.Trim()));
        Assert.Empty(tree.FindAll("[role='alert']"));
        tree.Find("input").Input(query);
        tree.Find("input").Input("");
        tree.WaitForAssertion(() => Assert.Equal(201, tree.FindAll("[role='treeitem']").Count));
        Assert.Equal(5, source.ServiceReads);
        Assert.Empty(tree.FindAll("[role='alert']"));
    }

    [Fact]
    public void SearchAcceptsTheTrimmedLengthBoundaryWithoutChangingTheLiteralQuery()
    {
        var source = new PublicationNavigationSource();
        using var context = PublicationLayoutContext(source);
        var tree = context.Render<ConsolePublicationNavigation>();
        tree.WaitForAssertion(() => Assert.Equal(1, source.ServiceReads));
        var query = string.Concat(Enumerable.Repeat("é.*[", 50));
        tree.Find("input").Input(" " + query + " ");
        tree.WaitForAssertion(() => Assert.Equal(query, source.LastQuery.NameQuery));
        Assert.Empty(tree.FindAll("[role='alert']"));
    }

    [Fact]
    public void GroupedSearchOfALoadedCatalogPagesServicesWithoutReadingAndClearsBackToTree()
    {
        var source = new PublicationNavigationSource();
        using var context = PublicationLayoutContext(source);
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = true });
        var tree = context.Render<ConsolePublicationNavigation>();
        tree.WaitForAssertion(() => Assert.Equal(201, tree.FindAll("[role='treeitem']").Count));
        tree.Find("input").Input(" service ");
        tree.WaitForAssertion(() => Assert.Single(tree.FindAll("[data-publication-search]")));
        tree.WaitForAssertion(() => Assert.Equal(["Folders (0)", "Services (500)", "Layers (0)"],
            tree.FindAll("[data-publication-search] h3").Select(element => element.TextContent.Trim())));
        Assert.Equal(25, tree.FindAll("[role='treeitem']").Count);
        Assert.Equal(5, source.ServiceReads);
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
        Assert.Equal(1, source.ConnectionReads);
        Assert.Equal(["service"], source.LayerQueries);
        Assert.All(tree.FindAll("[data-tree-link]"), link => Assert.StartsWith("/operate/services/", link.GetAttribute("href")));
        tree.Find("input").Input("absent");
        tree.WaitForAssertion(() => Assert.Contains("No services match this search.", tree.Markup));
        Assert.Empty(tree.FindAll("[data-search-more]"));
        tree.Find("input").Input("");
        tree.WaitForAssertion(() => Assert.Empty(tree.FindAll("[data-publication-search]")));
        Assert.Equal(201, tree.FindAll("[role='treeitem']").Count);
        Assert.Equal(5, source.ServiceReads);
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
        Assert.Equal(1, source.ServiceReads);
        Assert.Equal(" roads ", tree.Find("input").GetAttribute("value"));
    }

    [Fact]
    public void PendingServerSearchHidesServicesThatDoNotMatchTheNewQuery()
    {
        var source = HoldingSecondPage(new PublicationNavigationSource());
        using var context = PublicationLayoutContext(source);
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = true });
        var tree = context.Render<ConsolePublicationNavigation>();
        tree.WaitForAssertion(() => Assert.Equal(101, tree.FindAll("[role='treeitem']").Count));
        source.HoldCount = 1;
        tree.Find("input").Input("absent");
        tree.WaitForAssertion(() => Assert.Equal(3, source.ServiceReads));
        Assert.Empty(tree.FindAll("[role='treeitem']"));
        Assert.Equal("Services", tree.Find("#console-search-services").TextContent.Trim());
        Assert.Contains("Searching…", tree.Markup);
        Assert.DoesNotContain("No services match this search.", tree.Markup);
        Assert.Empty(tree.FindAll("[data-search-more], .console-search-summary"));
        source.ReleaseNext();
        source.ReleaseNext();
        tree.WaitForAssertion(() => Assert.Contains("Services <span>(0)</span>", tree.Markup));
    }

    [Fact]
    public void ServerSearchReadsTheNextPageWhenLoadedServicesDoNotAllMatch()
    {
        var source = HoldingSecondPage(new PublicationNavigationSource
        {
            IgnoreNameQuery = true,
            ServiceSummaries = Enumerable.Range(0, 500)
                .Select(index => new Honua.Sdk.Admin.Models.ServiceSummary
                {
                    ServiceName = $"{(index % 4 == 0 ? "wanted" : "other")}-{index:D3}"
                }).ToArray()
        });
        using var context = PublicationLayoutContext(source);
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = true });
        var tree = context.Render<ConsolePublicationNavigation>();
        tree.WaitForAssertion(() => Assert.Equal(101, tree.FindAll("[role='treeitem']").Count));
        tree.Find("input").Input("wanted");
        tree.WaitForAssertion(() => Assert.Equal(25, tree.FindAll("[data-publication-search] [role='treeitem']").Count));
        Assert.Equal(3, source.ServiceReads);
        tree.Find("[data-search-more]").Click();
        tree.WaitForAssertion(() => Assert.Equal(4, source.ServiceReads));
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
        Assert.StartsWith("8 matches in Services", tree.Find(".console-search-summary").TextContent.Trim());
    }

    internal static PublicationNavigationSource HoldingSecondPage(PublicationNavigationSource source)
    {
        source.OnServiceRead = () => { if (source.ServiceReads == 2) source.HoldCount = 1; };
        return source;
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
