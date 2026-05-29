using Bunit;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.Services;

namespace Honua.Console.Native.Core.Tests.Components;

public sealed class OperateSectionStatusPanelTests : ConsoleComponentTestBase
{
    [Fact]
    public void Loading_takes_precedence_and_renders_loading_state()
    {
        var cut = Render<OperateSectionStatusPanel>(p => p
            .Add(c => c.Heading, "Services")
            .Add(c => c.Loading, true)
            // Empty/Status are ignored while Loading is true.
            .Add(c => c.Empty, true)
            .Add(c => c.Status, OperateSectionStatus.Forbidden));

        Assert.Contains("operate-status-loading", cut.Find(".operate-status-panel").ClassList);
        cut.Find(".console-kicker").TextContent.MarkupMatches("Services");
        cut.Find("h3").TextContent.MarkupMatches("Loading...");
        cut.Find(".console-muted").TextContent
            .MarkupMatches("Reading live data from the connected honua-server.");
    }

    [Fact]
    public void Empty_renders_empty_state_with_custom_copy()
    {
        var cut = Render<OperateSectionStatusPanel>(p => p
            .Add(c => c.Heading, "Connections")
            .Add(c => c.Empty, true)
            .Add(c => c.EmptyTitle, "No connections yet")
            .Add(c => c.EmptyMessage, "Add a connection to get started."));

        Assert.Contains("operate-status-empty", cut.Find(".operate-status-panel").ClassList);
        cut.Find("h3").TextContent.MarkupMatches("No connections yet");
        cut.Find(".console-muted").TextContent.MarkupMatches("Add a connection to get started.");
    }

    [Theory]
    [InlineData(OperateSectionStatus.Missing, "Not found")]
    [InlineData(OperateSectionStatus.Forbidden, "Permission required")]
    [InlineData(OperateSectionStatus.Unsupported, "Unsupported by this server")]
    [InlineData(OperateSectionStatus.Unavailable, "Temporarily unavailable")]
    public void Status_drives_shared_title_when_not_loading_or_empty(
        OperateSectionStatus status,
        string expectedTitle)
    {
        var cut = Render<OperateSectionStatusPanel>(p => p
            .Add(c => c.Heading, "Events")
            .Add(c => c.Status, status));

        Assert.Contains("operate-status-denied", cut.Find(".operate-status-panel").ClassList);
        cut.Find("h3").TextContent.MarkupMatches(expectedTitle);
    }

    [Fact]
    public void Server_message_overrides_fallback_copy()
    {
        var cut = Render<OperateSectionStatusPanel>(p => p
            .Add(c => c.Heading, "Jobs")
            .Add(c => c.Status, OperateSectionStatus.Unavailable)
            .Add(c => c.Message, "Upstream timed out after 30s."));

        cut.Find(".console-muted").TextContent.MarkupMatches("Upstream timed out after 30s.");
    }

    [Fact]
    public void Blank_message_falls_back_to_shared_status_copy()
    {
        var cut = Render<OperateSectionStatusPanel>(p => p
            .Add(c => c.Heading, "Jobs")
            .Add(c => c.Status, OperateSectionStatus.Forbidden)
            .Add(c => c.Message, "  "));

        cut.Find(".console-muted").TextContent.MarkupMatches(
            OperateSectionPresentation.FallbackMessage(OperateSectionStatus.Forbidden));
    }
}
