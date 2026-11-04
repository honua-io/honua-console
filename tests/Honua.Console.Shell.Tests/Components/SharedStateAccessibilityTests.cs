using Bunit;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.Components.Operate;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.Shell.Tests.Components;

public sealed class SharedStateAccessibilityTests : ConsoleComponentTestBase
{
    [Theory]
    [InlineData(1, "h1")]
    [InlineData(2, "h2")]
    [InlineData(3, "h3")]
    public void StateHeadingPreservesTheComposingPagesOwnership(int headingLevel, string selector)
    {
        var state = Render<ConsoleStateView>(parameters => parameters
            .Add(component => component.HeadingLevel, headingLevel)
            .Add(component => component.Title, "Data is unavailable"));

        Assert.Single(state.FindAll("h1, h2, h3"));
        Assert.Equal("Data is unavailable", state.Find(selector).TextContent);
        Assert.Equal(headingLevel == 1 ? 1 : 0, state.FindAll("h1").Count);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void DeniedCapabilityPreservesHeadingAndActionWithoutMountingChildren(int headingLevel)
    {
        Services.AddSingleton<IConsoleCapabilityManifest>(new ConsoleCapabilityManifest());
        var mounted = false;
        var gate = Render<ConsoleCapabilityGate>(parameters => parameters
            .Add(component => component.Capability, ConsoleCapabilityKeys.Temporal)
            .Add(component => component.HeadingLevel, headingLevel)
            .Add(component => component.ActionHref, "/operate/data")
            .Add(component => component.ActionLabel, "Open data")
            .AddChildContent(builder => { mounted = true; }));

        Assert.False(mounted);
        Assert.Single(gate.FindAll($"h{headingLevel}"));
        Assert.Equal("/operate/data", gate.Find("a").GetAttribute("href"));
    }

    [Fact]
    public void TableRegionKeepsKeyboardAccessAndItsPurpose()
    {
        var table = Render<ConsoleTableRegion>(parameters => parameters
            .Add(component => component.Label, "Service layers")
            .Add(component => component.Class, "operate-table-scroll")
            .AddChildContent("<table><caption>Published layers</caption></table>"));

        var region = table.Find("[role=region]");
        Assert.Equal("0", region.GetAttribute("tabindex"));
        Assert.Equal("Service layers", region.GetAttribute("aria-label"));
        Assert.Equal("Published layers", table.Find("caption").TextContent);
    }

    [Fact]
    public void ResourceSelectionUsesNativeButtonsAndRetainsCanonicalIdentity()
    {
        ResourceTreeNode? selected = null;
        var resource = new ResourceTreeNode("connection-layer-7", "Flood zones", "PostGIS", "Running", 4, "Polygon",
            [new PublicationTreeNode("FeatureServer", "FeatureServer", true, "/rest/services/emergency/FeatureServer/7")]);
        var navigation = Render<ResourcePublicationsTree>(parameters => parameters
            .Add(component => component.Nodes, [resource])
            .Add(component => component.OnResourceSelected, node => selected = node));

        var selector = navigation.Find("button.resource-tree__name");
        Assert.Equal("false", selector.GetAttribute("aria-expanded"));
        selector.Click();

        Assert.Same(resource, selected);
        Assert.Equal("true", navigation.Find("button.resource-tree__name").GetAttribute("aria-expanded"));
        Assert.Single(navigation.FindAll("[data-publication=FeatureServer] button"));
        Assert.Empty(navigation.FindAll("[role=tree], [role=treeitem]"));
        Assert.Equal("Resources", navigation.Find("ul").GetAttribute("aria-label"));
    }
}
