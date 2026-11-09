using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class StudioAutomationsPageTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnboundRuntime_RendersMissingBindingState_NotEmpty(bool v4Enabled)
    {
        using var ctx = new BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton(new ConsoleV4Options { Enabled = v4Enabled });
        ctx.Services.AddSingleton<ICollectAutomationClient>(new UnsupportedCollectAutomationClient());

        var page = ctx.Render<StudioAutomationsPage>();

        page.WaitForAssertion(
            () => Assert.NotEmpty(page.FindAll("section.console-state-error")),
            TimeSpan.FromSeconds(5));
        Assert.Contains("Automation authoring is not bound", page.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("No automations yet", page.Markup, StringComparison.Ordinal);
        Assert.Equal(v4Enabled, page.Find("section.automation-page").ClassList.Contains("console-automations-v4"));
        Assert.Equal(v4Enabled, page.FindAll("section > link[rel='stylesheet'][href$='studio-automations-v4.css']").Count == 1);
        if (v4Enabled)
        {
            CatalogAdminPreview.Capture("automations-unbound", page.Markup);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BoundRuntime_ListsSeededAutomationWithRuleCountAndVersion(bool v4Enabled)
    {
        using var ctx = new BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton(new ConsoleV4Options { Enabled = v4Enabled });
        ctx.Services.AddSingleton<ICollectAutomationClient>(InMemoryCollectAutomationClient.CreateSeeded());

        var page = ctx.Render<StudioAutomationsPage>();

        page.WaitForAssertion(
            () => Assert.NotEmpty(page.FindAll(v4Enabled ? "tbody th a" : "a.automation-card")),
            TimeSpan.FromSeconds(5));
        Assert.Contains("Permit intake automation", page.Markup, StringComparison.Ordinal);
        Assert.Contains("New automation", page.Markup, StringComparison.Ordinal);

        var automationLink = page.FindAll(v4Enabled ? "tbody th a" : "a.automation-card")
            .Single(link => link.TextContent.Contains("Permit intake automation", StringComparison.Ordinal));
        Assert.StartsWith("/studio/automations/", automationLink.GetAttribute("href"), StringComparison.Ordinal);
        Assert.Equal(v4Enabled, page.Find("section.automation-page").ClassList.Contains("console-automations-v4"));
        Assert.Equal("/studio/automations/new", page.Find(".automation-list-actions a").GetAttribute("href"));

        if (v4Enabled)
        {
            var row = automationLink.Closest("tr")!;
            var cells = row.QuerySelectorAll("td");
            Assert.Equal("Enabled", cells[0].TextContent.Trim());
            Assert.Equal("v1", cells[2].TextContent.Trim());
            Assert.True(int.Parse(cells[1].TextContent.Trim(), System.Globalization.CultureInfo.InvariantCulture) > 0);
            Assert.NotNull(cells[3].QuerySelector("time[datetime]"));
            CatalogAdminPreview.Capture("automations-seeded", page.Markup);
        }
        else
        {
            Assert.Contains("v1", automationLink.TextContent, StringComparison.Ordinal);
        }
    }
}
