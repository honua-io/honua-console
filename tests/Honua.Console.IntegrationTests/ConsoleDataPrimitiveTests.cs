using Bunit;
using Honua.Console.Shell.Components;

namespace Honua.Console.IntegrationTests;

public sealed class ConsoleDataPrimitiveTests
{
    [Fact]
    public void NameIdentifier_PreservesFullNameAndSelectableIdentifier()
    {
        using var context = new BunitContext();
        var page = context.Render<ConsoleNameIdentifier>(parameters => parameters
            .Add(component => component.Name, "Public roads")
            .Add(component => component.Identifier, "roads/public-with-a-long-stable-identifier"));

        Assert.Equal("Public roads", page.Find(".console-name-identifier-name").TextContent);
        Assert.Equal("roads/public-with-a-long-stable-identifier", page.Find(".console-name-identifier-id").TextContent);
        Assert.Empty(page.FindAll("a, [aria-hidden]"));
    }

    [Fact]
    public void NameIdentifier_UsesCallerLinkAndOmitsAbsentIdentifier()
    {
        using var context = new BunitContext();
        var page = context.Render<ConsoleNameIdentifier>(parameters => parameters
            .Add(component => component.Name, "Public roads")
            .Add(component => component.Href, "/roads"));

        Assert.Equal("/roads", page.Find("a").GetAttribute("href"));
        Assert.Equal("Public roads", page.Find("a").TextContent);
        Assert.Empty(page.FindAll(".console-name-identifier-id"));
    }

    [Fact]
    public void ProtocolList_PreservesCallerOrderAndVocabulary()
    {
        using var context = new BunitContext();
        var page = context.Render<ConsoleProtocolList>(parameters => parameters
            .Add(component => component.Protocols, new[] { "WFS 2.0", "WMS 1.3" })
            .Add(component => component.Label, "Published protocols"));

        Assert.Equal("Published protocols", page.Find("ul").GetAttribute("aria-label"));
        Assert.Equal(["WFS 2.0", "WMS 1.3"], page.FindAll("li").Select(item => item.TextContent));
        Assert.Empty(page.FindAll("a, button"));
    }

    [Fact]
    public void ProtocolList_EmptyInputDoesNotInventAvailability()
    {
        using var context = new BunitContext();
        var page = context.Render<ConsoleProtocolList>();

        Assert.Empty(page.Markup);
    }
}
