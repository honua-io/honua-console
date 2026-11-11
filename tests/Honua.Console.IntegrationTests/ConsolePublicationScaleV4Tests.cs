using Bunit;
using Honua.Console.Shell.Layout;
using Honua.Console.Shell.Models;
using Microsoft.Extensions.DependencyInjection;

using static Honua.Console.IntegrationTests.ConsolePublicationRenderFixture;

namespace Honua.Console.IntegrationTests;

public sealed class ConsolePublicationScaleV4Tests
{
    [Theory]
    [InlineData(ConsolePresentationMode.Legacy)]
    [InlineData(ConsolePresentationMode.V4)]
    public void ScaleLayoutPreservesPagingLazyLayersAndServerNameFiltering(ConsolePresentationMode presentation)
    {
        var source = new PublicationNavigationSource();
        using var context = PublicationLayoutContext(source);
        var options = new ConsoleV4Options { Enabled = presentation == ConsolePresentationMode.V4 };
        context.Services.AddSingleton(options);
        var header = context.Render<ConsoleTopNavigation>();
        var tree = context.Render<ConsolePublicationNavigation>();
        tree.WaitForAssertion(() => Assert.Equal(26, tree.FindAll("[role='treeitem']").Count));
        Assert.Equal(0, source.ChildReads);
        Assert.Equal(0, source.ConnectionReads);
        Assert.Equal(options.Enabled ? "Search services" : "Filter services", tree.Find("label").TextContent);
        Assert.Equal(options.Enabled ? "Search service names…" : null, tree.Find("input").GetAttribute("placeholder"));
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
        Assert.Equal(options.Enabled ? 1 : 2, tree.FindAll("[role='treeitem']").Count);
        Assert.Equal("service-499", source.LastQuery.NameQuery);
        Assert.Equal(1, source.ChildReads);
        Capture((tree.Markup, header.Markup), presentation, "search");
        tree.Find("input").Input("absent-service");
        tree.WaitForAssertion(() => Assert.Contains(options.Enabled ? "No services match this search." : "No services match this filter.", tree.Markup));
        Capture((tree.Markup, header.Markup), presentation, "empty");
        tree.Find("input").Input("");
        tree.WaitForAssertion(() => Assert.Contains("500 service names and 5000 layers", tree.Markup));
        Assert.Equal(1, source.ChildReads);
    }

    private static void Capture((string Tree, string Header) markup, ConsolePresentationMode presentation, string state)
    {
        var flag = presentation == ConsolePresentationMode.V4 ? "true" : "false";
        CatalogAdminPreview.Capture($"scale-{presentation.ToString().ToLowerInvariant()}-{state}",
            $"<div class=\"console-shell\" data-console-v4=\"{flag}\">{markup.Header}<div class=\"console-service-workspace\">{markup.Tree}<div class=\"console-workspace\"><main class=\"console-main\"><h1>Services</h1><p>500 services. Select a service to inspect its layers.</p></main></div></div></div>");
    }
}
