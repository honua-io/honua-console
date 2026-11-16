using Bunit;
using Honua.Console.Shell.Layout;

namespace Honua.Console.Shell.Tests.Components;

public sealed class ConsoleConversationDrawerTests
{
    [Fact]
    public void UnavailableConnectionExplainsStateWithoutWorkspaceActions()
    {
        using var context = new BunitContext();
        var drawer = context.Render<ConsoleConversationDrawer>(parameters => parameters
            .Add(component => component.IsAvailable, false));

        Assert.Contains("Conversation is unavailable", drawer.Find("[role='status']").TextContent);
        Assert.Equal("unavailable", drawer.Find("[data-agent-workspace]").GetAttribute("data-workspace-availability"));
        Assert.Empty(drawer.FindAll("textarea, [data-workspace-start], [data-workspace-connect], [data-workspace-send]"));
        Assert.Single(drawer.FindAll("[data-conversation-close]"));
    }

    [Fact]
    public void LegacyHostRetainsConversationControlsByDefault()
    {
        using var context = new BunitContext();
        var drawer = context.Render<ConsoleConversationDrawer>();

        Assert.Equal("available", drawer.Find("[data-agent-workspace]").GetAttribute("data-workspace-availability"));
        Assert.Single(drawer.FindAll("[data-workspace-prompt]"));
        Assert.Single(drawer.FindAll("[data-workspace-connect]"));
    }
}
