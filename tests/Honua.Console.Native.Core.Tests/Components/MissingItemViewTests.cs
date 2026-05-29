using Bunit;
using Honua.Console.Shell.Components;

namespace Honua.Console.Native.Core.Tests.Components;

public sealed class MissingItemViewTests : ConsoleComponentTestBase
{
    [Fact]
    public void Renders_default_kind_titlecased()
    {
        var cut = Render<MissingItemView>();

        cut.Find("h1").TextContent.MarkupMatches("Item Not Found");
        Assert.Contains("The requested item is not available.", cut.Markup);
    }

    [Fact]
    public void Titlecases_multi_word_kind_per_word()
    {
        var cut = Render<MissingItemView>(p => p
            .Add(c => c.Kind, "saved map"));

        cut.Find("h1").TextContent.MarkupMatches("Saved Map Not Found");
        Assert.Contains("The requested saved map is not available.", cut.Markup);
    }

    [Fact]
    public void Renders_area_label_when_provided()
    {
        var cut = Render<MissingItemView>(p => p
            .Add(c => c.Kind, "service")
            .Add(c => c.AreaLabel, "Operate"));

        cut.Find(".console-kicker").TextContent.MarkupMatches("Operate");
    }

    [Fact]
    public void Omits_area_label_when_blank()
    {
        var cut = Render<MissingItemView>(p => p
            .Add(c => c.AreaLabel, " "));

        Assert.Empty(cut.FindAll(".console-kicker"));
    }
}
