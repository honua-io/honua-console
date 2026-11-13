using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class ConsoleUtilityPagesV4Tests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LocalSetupPreservesAgentHandoffAndConnectionLink(bool enabled)
    {
        using var context = CreateContext(enabled);
        var page = context.Render<LocalSetupPage>();
        Assert.Equal("Start Honua with your local agent", page.Find("h1").TextContent);
        Assert.Equal(enabled ? 1 : 0, page.FindAll(".console-page-heading-v4").Count);
        Assert.Contains("Check Docker on this computer", page.Find("[data-local-agent-prompt]").TextContent);
        Assert.Equal("Copy request for my local agent", page.Find("[data-agent-handoff]").TextContent);
        Assert.Equal("/environments/new", page.Find("a[href$='/environments/new']").GetAttribute("href"));
        Capture(page.Markup, "setup", enabled);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingRoutePreservesHomeRecovery(bool enabled)
    {
        using var context = CreateContext(enabled);
        var page = context.Render<NotFoundPage>();
        Assert.Equal(enabled ? "Page not found" : "Page Not Found", page.Find("h1").TextContent);
        Assert.Equal(enabled ? 1 : 0, page.FindAll(".console-page-heading-v4").Count);
        Assert.Contains("requested Console route is not registered", page.Markup);
        Assert.Equal("/", page.Find("a").GetAttribute("href"));
        Assert.Equal("Return Home", page.Find("a").TextContent);
        Capture(page.Markup, "missing", enabled);
    }

    private static BunitContext CreateContext(bool enabled)
    {
        var context = new BunitContext();
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = enabled });
        return context;
    }

    private static void Capture(string markup, string name, bool enabled) =>
        CatalogAdminPreview.Capture($"theme-route-{name}-{(enabled ? "v4" : "legacy")}",
            $"<div class=\"console-shell\" data-console-v4=\"{enabled.ToString().ToLowerInvariant()}\">{markup}</div>");
}
