using Bunit;
using Honua.Console.Shell.Components;

namespace Honua.Console.IntegrationTests;

public sealed class ConsoleStatePresentationTests
{
    [Theory]
    [InlineData("info")]
    [InlineData("warning")]
    [InlineData("danger")]
    [InlineData("unsupported")]
    public void State_KeepsItsMessageHeadingAndRecoveryAction(string kind)
    {
        using var context = new BunitContext();
        var state = context.Render<HonuaState>(parameters => parameters
            .Add(component => component.Kind, kind)
            .Add(component => component.Title, "Review the selected environment")
            .Add(component => component.Message, "The selected environment needs attention.")
            .Add(component => component.ActionHref, "/environments")
            .Add(component => component.ActionLabel, "Review environment"));

        Assert.Equal("Review the selected environment", state.Find("h1").TextContent);
        Assert.Contains("The selected environment needs attention.", state.Find("p").TextContent);
        Assert.Equal("/environments", state.Find("a").GetAttribute("href"));
        Assert.Contains($"console-state-{kind}", state.Find("section").ClassList);
        CatalogAdminPreview.Capture($"state-{kind}", state.Markup);
    }
}
