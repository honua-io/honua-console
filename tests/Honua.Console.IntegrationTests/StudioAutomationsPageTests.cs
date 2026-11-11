using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class StudioAutomationsPageTests
{
    [Theory]
    [InlineData(ConsolePresentationMode.Legacy)]
    [InlineData(ConsolePresentationMode.V4)]
    public void UnboundRuntime_RendersMissingBindingState_NotEmpty(ConsolePresentationMode presentation)
    {
        using var ctx = new BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton(new ConsoleV4Options { Enabled = presentation == ConsolePresentationMode.V4 });
        ctx.Services.AddSingleton<ICollectAutomationClient>(new UnsupportedCollectAutomationClient());

        var page = ctx.Render<StudioAutomationsPage>();

        page.WaitForAssertion(
            () => Assert.NotEmpty(page.FindAll("section.console-state-error")),
            TimeSpan.FromSeconds(5));
        Assert.Contains("Automation authoring is not bound", page.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("No automations yet", page.Markup, StringComparison.Ordinal);
        Assert.Equal(presentation == ConsolePresentationMode.V4, page.Find("section.automation-page").ClassList.Contains("console-automations-v4"));
        Assert.Equal(presentation == ConsolePresentationMode.V4, page.FindAll("section > link[rel='stylesheet'][href$='studio-automations-v4.css']").Count == 1);
        Assert.Equal(presentation == ConsolePresentationMode.V4, page.FindAll(".console-page-heading-v4").Count == 1);
        if (presentation == ConsolePresentationMode.V4)
        {
            Assert.Single(page.FindAll("h1"));
            CatalogAdminPreview.Capture("automations-unbound", $"<div class=\"console-shell\" data-console-v4=\"true\">{page.Markup}</div>");
        }
    }

    [Theory]
    [InlineData(ConsolePresentationMode.Legacy)]
    [InlineData(ConsolePresentationMode.V4)]
    public async Task BoundRuntime_ListsSeededAutomationWithRuleCountAndVersion(ConsolePresentationMode presentation)
    {
        var registry = new ConsoleAgentContextRegistry();
        using var ctx = new BunitContext();
        ctx.Services.AddSingleton(registry);
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton(new ConsoleV4Options { Enabled = presentation == ConsolePresentationMode.V4 });
        ctx.Services.AddSingleton<ICollectAutomationClient>(InMemoryCollectAutomationClient.CreateSeeded());

        ctx.Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().NavigateTo("/studio/automations?secret=private");

        var page = ctx.Render<StudioAutomationsPage>();

        page.WaitForAssertion(
            () => Assert.NotEmpty(page.FindAll(presentation == ConsolePresentationMode.V4 ? "tbody th a" : "a.automation-card")),
            TimeSpan.FromSeconds(5));
        Assert.Contains("Permit intake automation", page.Markup, StringComparison.Ordinal);
        Assert.Contains("New automation", page.Markup, StringComparison.Ordinal);

        var automationLink = page.FindAll(presentation == ConsolePresentationMode.V4 ? "tbody th a" : "a.automation-card")
            .Single(link => link.TextContent.Contains("Permit intake automation", StringComparison.Ordinal));
        Assert.StartsWith("/studio/automations/", automationLink.GetAttribute("href"), StringComparison.Ordinal);
        Assert.Equal(presentation == ConsolePresentationMode.V4, page.Find("section.automation-page").ClassList.Contains("console-automations-v4"));
        Assert.Equal("/studio/automations/new", page.Find(".automation-list-actions a").GetAttribute("href"));

        if (presentation == ConsolePresentationMode.V4)
        {
            var row = automationLink.Closest("tr")!;
            var cells = row.QuerySelectorAll("td");
            Assert.Equal("Enabled", cells[0].TextContent.Trim());
            Assert.Equal("v1", cells[2].TextContent.Trim());
            Assert.True(int.Parse(cells[1].TextContent.Trim(), System.Globalization.CultureInfo.InvariantCulture) > 0);
            Assert.NotNull(cells[3].QuerySelector("time[datetime]"));
            Assert.Single(page.FindAll("h1"));
            Assert.Single(page.FindAll(".console-page-heading-v4"));
            CatalogAdminPreview.Capture("automations-seeded", $"<div class=\"console-shell\" data-console-v4=\"true\">{page.Markup}</div>");
        }
        else
        {
            Assert.Contains("v1", automationLink.TextContent, StringComparison.Ordinal);
        }
        if (presentation == ConsolePresentationMode.V4)
        {
            Assert.Equal("/studio/automations", registry.Current.Route);
            Assert.Empty(registry.Current.Entities);
        }
        else Assert.Equal(0, registry.Current.Generation);
        await ctx.DisposeComponentsAsync();
        Assert.Empty(registry.Current.Filters);
        Assert.Equal(0, registry.Current.Unsaved.ChangedFieldCount);

    }
}
