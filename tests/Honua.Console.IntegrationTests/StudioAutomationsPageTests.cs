using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free render regression for the Collect automation list page (<see cref="StudioAutomationsPage"/>,
/// honua-console#219). An unbound runtime must render the shared missing-binding state (never a misleading
/// "empty"); a bound runtime lists the automations with their rule count + version badge.
/// </summary>
public sealed class StudioAutomationsPageTests
{
    [Fact]
    public void UnboundRuntime_RendersMissingBindingState_NotEmpty()
    {
        using var ctx = new BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<ICollectAutomationClient>(new UnsupportedCollectAutomationClient());

        var page = ctx.Render<StudioAutomationsPage>();

        page.WaitForAssertion(
            () => Assert.NotEmpty(page.FindAll("section.console-state-error")),
            TimeSpan.FromSeconds(5));
        Assert.Contains("Automation authoring is not bound", page.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("No automations yet", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void BoundRuntime_ListsSeededAutomationWithRuleCountAndVersion()
    {
        using var ctx = new BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<ICollectAutomationClient>(InMemoryCollectAutomationClient.CreateSeeded());

        var page = ctx.Render<StudioAutomationsPage>();

        page.WaitForAssertion(
            () => Assert.NotEmpty(page.FindAll("a.automation-card")),
            TimeSpan.FromSeconds(5));
        Assert.Contains("Permit intake automation", page.Markup, StringComparison.Ordinal);
        Assert.Contains("New automation", page.Markup, StringComparison.Ordinal);

        // The card deep-links into the editor by draft id.
        var card = page.Find("a.automation-card");
        Assert.StartsWith("/studio/automations/", card.GetAttribute("href"), StringComparison.Ordinal);
    }
}
