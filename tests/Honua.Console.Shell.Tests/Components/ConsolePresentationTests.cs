using Bunit;
using Honua.Console.Shell.Components;

namespace Honua.Console.Shell.Tests.Components;

public sealed class ConsolePresentationTests : ConsoleComponentTestBase
{
    [Fact]
    public void PageHeading_KeepsThePageTitleAndActionsInDocumentOrder()
    {
        var cut = Render<ConsolePageHeading>(parameters => parameters
            .Add(component => component.Title, "Services")
            .Add(component => component.ChildContent, "<p>Published on this server.</p>")
            .Add(component => component.Actions, "<a href='/catalog/services/new'>Add service</a>"));

        Assert.Equal("Services", cut.Find("h1").TextContent);
        Assert.Equal("Published on this server.", cut.Find("h1 + p").TextContent);
        Assert.Equal("/catalog/services/new", cut.Find(".console-page-heading-actions a").GetAttribute("href"));
        Assert.Single(cut.FindAll("h1"));
    }

    [Fact]
    public void Panel_ExposesItsHeadingAsTheAccessibleName()
    {
        var cut = Render<ConsolePanel>(parameters => parameters
            .Add(component => component.Title, "Administration")
            .Add(component => component.HeadingId, "home-administration")
            .Add(component => component.ChildContent, "<p>Manage access.</p>")
            .Add(component => component.Actions, "<button type='button'>Refresh</button>"));

        var headingId = cut.Find("section").GetAttribute("aria-labelledby");
        Assert.Equal("Administration", cut.Find($"h2#{headingId}").TextContent);
        Assert.Equal("Refresh", cut.Find("button").TextContent);
        Assert.Contains("Manage access.", cut.Find("section").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void MetricTile_ReportsUnavailableRatherThanInventingZero()
    {
        var cut = Render<ConsoleMetricTile>(parameters => parameters
            .Add(component => component.Label, "Layers"));

        Assert.Equal("Layers", cut.Find("dt").TextContent);
        Assert.Equal("Not available", cut.Find("dd").TextContent);
    }

    [Fact]
    public void MetricTile_PreservesTheSuppliedUnavailableReason()
    {
        var cut = Render<ConsoleMetricTile>(parameters => parameters
            .Add(component => component.Label, "Layers")
            .Add(component => component.UnavailableText, "Access required"));

        Assert.Equal("Access required", cut.Find("dd").TextContent);
    }

    [Fact]
    public void MetricTile_PreservesAnActualZeroAndContextLink()
    {
        var cut = Render<ConsoleMetricTile>(parameters => parameters
            .Add(component => component.Label, "Layers")
            .Add(component => component.Value, 0)
            .Add(component => component.ChildContent, "<a href='/catalog/layers'>Browse layers</a>"));

        Assert.Equal("0", cut.Find("dd").TextContent);
        Assert.Equal("/catalog/layers", cut.Find("a").GetAttribute("href"));
    }

    [Fact]
    public void MetricTile_EncodesServerSuppliedLabels()
    {
        var cut = Render<ConsoleMetricTile>(parameters => parameters
            .Add(component => component.Label, "<script>Services</script>")
            .Add(component => component.Value, 12));

        Assert.Empty(cut.FindAll("script"));
        Assert.Equal("<script>Services</script>", cut.Find("dt").TextContent);
        Assert.Equal("12", cut.Find("dd").TextContent);
    }
}
