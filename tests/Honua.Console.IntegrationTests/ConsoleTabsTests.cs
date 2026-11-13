using Bunit;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.Models;

namespace Honua.Console.IntegrationTests;

public sealed class ConsoleTabsTests
{
    private static readonly ConsoleTab[] Tabs = [new("layers-tab", "Layers", "layers-panel"), new("access-tab", "Access", "access-panel")];

    [Fact]
    public void Tabs_ReportSelectionToCallerAndExposePanelRelationships()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        string? selected = null;
        var page = context.Render<ConsoleTabs>(parameters => parameters
            .Add(component => component.Tabs, Tabs)
            .Add(component => component.SelectedId, "layers-tab")
            .Add(component => component.Label, "Service")
            .Add(component => component.SelectedIdChanged, value => selected = value));

        Assert.Equal("Service", page.Find("[role='tablist']").GetAttribute("aria-label"));
        Assert.Equal("layers-panel", page.Find("#layers-tab").GetAttribute("aria-controls"));
        Assert.Equal("0", page.Find("#layers-tab").GetAttribute("tabindex"));
        Assert.Equal("-1", page.Find("#access-tab").GetAttribute("tabindex"));
        page.Find("#access-tab").Click();
        Assert.Equal("access-tab", selected);
        Assert.Equal("true", page.Find("#layers-tab").GetAttribute("aria-selected"));
        Assert.Empty(page.FindAll("a"));
    }

    [Fact]
    public void Tabs_RejectAmbiguousPanelAndTabIds()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        Assert.Throws<InvalidOperationException>(() => context.Render<ConsoleTabs>(parameters => parameters
            .Add(component => component.Tabs, new[] { new ConsoleTab("layers-tab", "Layers", "layers-tab") })
            .Add(component => component.SelectedId, "layers-tab")
            .Add(component => component.Label, "Service")));
    }
}
