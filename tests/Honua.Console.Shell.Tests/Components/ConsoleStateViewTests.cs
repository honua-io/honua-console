using Bunit;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Tests.Components;

public sealed class ConsoleStateViewTests : ConsoleComponentTestBase
{
    [Fact]
    public void Renders_defaults_with_empty_kind_class_and_aria_live()
    {
        var cut = Render<ConsoleStateView>();

        var section = cut.Find("section");
        Assert.Contains("console-state", section.ClassList);
        Assert.Contains("console-state-empty", section.ClassList);
        Assert.Equal("polite", section.GetAttribute("aria-live"));

        cut.Find(".console-kicker").TextContent.MarkupMatches("Console state");
        cut.Find("h1").TextContent.MarkupMatches("Nothing to show");
        cut.Find("section > p:not(.console-kicker)").TextContent
            .MarkupMatches("This route did not return content.");
    }

    [Theory]
    [InlineData("error", "console-state-error")]
    [InlineData("forbidden", "console-state-forbidden")]
    [InlineData("loading", "console-state-loading")]
    public void Kind_drives_state_modifier_class(string kind, string expectedClass)
    {
        var cut = Render<ConsoleStateView>(p => p.Add(c => c.Kind, kind));

        Assert.Contains(expectedClass, cut.Find("section").ClassList);
    }

    [Fact]
    public void Informational_treatment_preserves_read_state_and_actions()
    {
        var cut = Render<ConsoleStateView>(p => p
            .Add(c => c.Treatment, ConsoleStateTreatment.Informational)
            .Add(c => c.Kind, "unavailable")
            .Add(c => c.HeadingLevel, 3)
            .Add(c => c.Title, "Catalog unavailable")
            .Add(c => c.ActionHref, "/catalog")
            .Add(c => c.ActionLabel, "Try again"));

        var section = cut.Find("section");
        Assert.Contains("console-state-unavailable", section.ClassList);
        Assert.Contains("console-state-informational", section.ClassList);
        Assert.Equal("polite", section.GetAttribute("aria-live"));
        Assert.Equal("Catalog unavailable", cut.Find("h3").TextContent);
        Assert.Equal("/catalog", cut.Find("a").GetAttribute("href"));
    }

    [Fact]
    public void Default_treatment_keeps_the_standard_state_presentation()
    {
        var cut = Render<ConsoleStateView>(p => p.Add(c => c.Kind, "unavailable"));

        Assert.DoesNotContain("console-state-informational", cut.Find("section").ClassList);
    }

    [Fact]
    public void Applies_title_kicker_and_message_parameters()
    {
        var cut = Render<ConsoleStateView>(p => p
            .Add(c => c.Kicker, "Catalog")
            .Add(c => c.Title, "Something went wrong")
            .Add(c => c.Message, "The catalog could not be loaded."));

        cut.Find(".console-kicker").TextContent.MarkupMatches("Catalog");
        cut.Find("h1").TextContent.MarkupMatches("Something went wrong");
        Assert.Contains("The catalog could not be loaded.", cut.Markup);
    }

    [Fact]
    public void Renders_action_link_only_when_both_href_and_label_present()
    {
        var hrefOnly = Render<ConsoleStateView>(p => p
            .Add(c => c.ActionHref, "/catalog"));
        Assert.Empty(hrefOnly.FindAll("a.console-button-link"));

        var withAction = Render<ConsoleStateView>(p => p
            .Add(c => c.ActionHref, "/catalog")
            .Add(c => c.ActionLabel, "Back to catalog"));

        var link = withAction.Find("a.console-button-link");
        Assert.Equal("/catalog", link.GetAttribute("href"));
        link.TextContent.MarkupMatches("Back to catalog");
    }
}
