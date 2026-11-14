using Bunit;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Tests.Components;

public sealed class ConsolePanelTests : ConsoleComponentTestBase
{
    [Fact]
    public void Default_panel_retains_visible_heading_and_boxed_surface()
    {
        var cut = Render<ConsolePanel>(p => p
            .Add(c => c.Title, "Layers")
            .Add(c => c.HeadingId, "layers"));

        Assert.Equal("layers", cut.Find("section").GetAttribute("aria-labelledby"));
        Assert.Equal("Layers", cut.Find("#layers").TextContent);
        Assert.DoesNotContain("console-panel-quiet", cut.Find("section").ClassList);
        Assert.False(cut.Find("h2").HasAttribute("class"));
        Assert.Empty(cut.FindAll(".console-panel-heading-concealed"));
    }

    [Fact]
    public void Quiet_panel_keeps_its_name_actions_and_content_when_heading_is_hidden()
    {
        var cut = Render<ConsolePanel>(p => p
            .Add(c => c.Title, "Layers")
            .Add(c => c.HeadingId, "layers")
            .Add(c => c.Surface, ConsolePanelSurface.Quiet)
            .Add(c => c.Heading, ConsolePanelHeading.VisuallyHidden)
            .Add(c => c.Actions, "<button>Add layer</button>")
            .AddChildContent("<p>Layer inventory</p>"));

        Assert.Contains("console-panel-quiet", cut.Find("section").ClassList);
        Assert.Equal("layers", cut.Find("section").GetAttribute("aria-labelledby"));
        Assert.Contains("console-panel-title-hidden", cut.Find("#layers").ClassList);
        Assert.Equal("Add layer", cut.Find(".console-panel-actions button").TextContent);
        Assert.Empty(cut.FindAll(".console-panel-heading-concealed"));
        Assert.Equal("Layer inventory", cut.Find("section > p").TextContent);
    }

    [Fact]
    public void Hidden_heading_without_actions_collapses_the_heading_row()
    {
        var cut = Render<ConsolePanel>(p => p
            .Add(c => c.Title, "Layers")
            .Add(c => c.HeadingId, "layers")
            .Add(c => c.Heading, ConsolePanelHeading.VisuallyHidden));

        Assert.Single(cut.FindAll(".console-panel-heading-concealed"));
        Assert.Equal("layers", cut.Find("section").GetAttribute("aria-labelledby"));
        Assert.Equal("Layers", cut.Find("#layers").TextContent);
    }
}
