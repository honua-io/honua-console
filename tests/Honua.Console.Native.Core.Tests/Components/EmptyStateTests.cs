using Bunit;
using Honua.Console.Shell.Components;

namespace Honua.Console.Native.Core.Tests.Components;

public sealed class EmptyStateTests : ConsoleComponentTestBase
{
    [Fact]
    public void Renders_defaults_with_titlecased_area_and_subject()
    {
        var cut = Render<EmptyState>();

        // Default Area "operate" is title-cased; default Subject is "items".
        cut.Find(".console-kicker").TextContent.MarkupMatches("Operate");
        cut.Find("h2").TextContent.MarkupMatches("No items");
        Assert.Contains("No items are available in this Operate environment.", cut.Markup);
    }

    [Fact]
    public void Applies_subject_and_area_parameters()
    {
        var cut = Render<EmptyState>(p => p
            .Add(c => c.Area, "catalog")
            .Add(c => c.Subject, "datasets"));

        cut.Find(".console-kicker").TextContent.MarkupMatches("Catalog");
        cut.Find("h2").TextContent.MarkupMatches("No datasets");
        Assert.Contains("No datasets are available in this Catalog environment.", cut.Markup);
    }

    [Fact]
    public void Omits_kicker_when_area_is_blank()
    {
        var cut = Render<EmptyState>(p => p.Add(c => c.Area, " "));

        Assert.Empty(cut.FindAll(".console-kicker"));
    }

    [Fact]
    public void Renders_action_link_only_when_both_href_and_text_present()
    {
        var withoutAction = Render<EmptyState>(p => p
            .Add(c => c.ActionHref, "/operate/services"));
        Assert.Empty(withoutAction.FindAll("a.console-button-link"));

        var withAction = Render<EmptyState>(p => p
            .Add(c => c.ActionHref, "/operate/services")
            .Add(c => c.ActionText, "Browse services"));

        var link = withAction.Find("a.console-button-link");
        Assert.Equal("/operate/services", link.GetAttribute("href"));
        link.TextContent.MarkupMatches("Browse services");
    }
}
