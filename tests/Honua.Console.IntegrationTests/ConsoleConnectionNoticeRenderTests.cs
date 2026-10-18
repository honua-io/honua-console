using Bunit;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.Models;

namespace Honua.Console.IntegrationTests;

public sealed class ConsoleConnectionNoticeRenderTests
{
    [Theory]
    [InlineData(401, "Sign in to read this environment", "/auth/login")]
    [InlineData(403, "Access to these resources was denied", "/environments")]
    public void CanonicalMissingPermissionRetainsActualHttpMeaning(int status, string heading, string href)
    {
        using var context = new BunitContext();
        var page = context.Render<ConsoleConnectionNotice>(parameters => parameters.Add(value => value.States,
            new[] { new OperateCapabilityState("Resources", "Missing permission", "contract", "Request denied") { StatusCode = status } }));
        Assert.Equal(heading, page.Find("h2").TextContent);
        Assert.Equal(href, page.Find("a").GetAttribute("href"));
    }

    [Theory]
    [InlineData("Missing binding", "Connect an environment to continue", "Connect environment", "/environments")]
    [InlineData("Unauthorized", "Sign in to read this environment", "Sign in", "/auth/login")]
    [InlineData("Forbidden", "Access to these resources was denied", "Choose environment", "/environments")]
    [InlineData("Unsupported", "This feature is unavailable on this deployment", "Review connection", "/environments")]
    [InlineData("Unavailable", "Could not read this environment", "Review connection", "/environments")]
    public void CapabilityFailure_HasRelevantPrimaryStatusAndAction(string state, string heading, string action, string href)
    {
        using var context = new BunitContext();
        var component = context.Render<ConsoleConnectionNotice>(parameters => parameters
            .Add(value => value.States, new[] { new OperateCapabilityState("Resources", state, "private-config-reference", "Detailed failure") }));
        Assert.Equal(heading, component.Find("h2").TextContent);
        Assert.Equal(action, component.Find("a").TextContent);
        Assert.Equal(href, component.Find("a").GetAttribute("href"));
        Assert.Null(component.Find("details").GetAttribute("open"));
        Assert.DoesNotContain("private-config-reference", component.Find("h2").TextContent);
    }
}
