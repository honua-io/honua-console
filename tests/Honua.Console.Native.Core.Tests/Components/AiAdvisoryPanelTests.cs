using Bunit;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.Models;

namespace Honua.Console.Native.Core.Tests.Components;

public sealed class AiAdvisoryPanelTests : ConsoleComponentTestBase
{
    private static OperateAiAdvisory Advisory(params string[] actions) =>
        new("Summary text", Array.Empty<OperateEvidenceLink>(), actions);

    [Fact]
    public void Renders_nothing_when_advisory_is_null()
    {
        var cut = Render<AiAdvisoryPanel>();

        Assert.Empty(cut.FindAll(".operate-ai-advisory"));
    }

    [Fact]
    public void Determinable_action_renders_as_a_link_to_the_operation()
    {
        var cut = Render<AiAdvisoryPanel>(p => p
            .Add(c => c.Advisory, Advisory("Open the publishing workspace to republish")));

        var link = cut.Find("a[data-ai-action]");
        Assert.Equal("/operate/publishing", link.GetAttribute("href"));
    }

    [Theory]
    [InlineData("Re-run the failed job", "/operate/geoprocessing")]
    [InlineData("Import the missing layers", "/operate/import/service")]
    [InlineData("Check the connection", "/operate/connections")]
    [InlineData("Review the alert", "/operate/observability")]
    public void Known_verbs_resolve_to_known_routes(string action, string expectedHref)
    {
        var cut = Render<AiAdvisoryPanel>(p => p
            .Add(c => c.Advisory, Advisory(action)));

        Assert.Equal(expectedHref, cut.Find("a[data-ai-action]").GetAttribute("href"));
    }

    [Fact]
    public void Undeterminable_action_raises_callback_when_wired()
    {
        string? selected = null;
        var cut = Render<AiAdvisoryPanel>(p => p
            .Add(c => c.Advisory, Advisory("Escalate to the on-call engineer"))
            .Add(c => c.OnActionSelected, action => selected = action));

        // No route is inferable, so it renders as a button that raises OnActionSelected.
        cut.Find("button[data-ai-action]").Click();

        Assert.Equal("Escalate to the on-call engineer", selected);
    }

    [Fact]
    public void Undeterminable_action_without_callback_is_static_text()
    {
        var cut = Render<AiAdvisoryPanel>(p => p
            .Add(c => c.Advisory, Advisory("Escalate to the on-call engineer")));

        Assert.Empty(cut.FindAll("a[data-ai-action]"));
        Assert.Empty(cut.FindAll("button[data-ai-action]"));
        Assert.Single(cut.FindAll("span[data-ai-action]"));
    }
}
