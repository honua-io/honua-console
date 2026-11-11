using Bunit;
using Microsoft.AspNetCore.Components;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class OperateOverviewPresentationTests
{
    public enum CapabilityAdvertisement { None, All }

    [Theory]
    [InlineData(ConsolePresentationMode.Legacy)]
    [InlineData(ConsolePresentationMode.V4)]
    public async Task AgentContextPublishesSafeRouteAndClearsOnDisposal(ConsolePresentationMode mode)
    {
        using var context = new BunitContext();
        var registry = new ConsoleAgentContextRegistry();
        context.Services.AddSingleton(registry);
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = mode == ConsolePresentationMode.V4 });
        context.Services.AddSingleton<IConsoleCapabilityManifest>(new ConsoleCapabilityManifest());
        context.Services.AddSingleton<IOperateTransitionDataSource>(new UnsupportedOperateTransitionDataSource());
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("/operate?token=private#secret");

        context.Render<OperatePage>();

        Assert.Equal(mode == ConsolePresentationMode.V4 ? "/operate" : "/", registry.Current.Route);
        Assert.Empty(registry.Current.Entities);
        Assert.Empty(registry.Current.Filters);
        Assert.Empty(registry.Current.Selection);
        Assert.Equal(0, registry.Current.Unsaved.ChangedFieldCount);
        Assert.Equal(0, registry.Current.Unsaved.PendingOperationCount);
        var generation = registry.Current.Generation;
        await context.DisposeComponentsAsync();
        if (mode == ConsolePresentationMode.V4) Assert.True(registry.Current.Generation > generation);
        else Assert.Equal(generation, registry.Current.Generation);
    }

    [Theory]
    [InlineData(ConsolePresentationMode.Legacy, CapabilityAdvertisement.None)]
    [InlineData(ConsolePresentationMode.Legacy, CapabilityAdvertisement.All)]
    [InlineData(ConsolePresentationMode.V4, CapabilityAdvertisement.None)]
    [InlineData(ConsolePresentationMode.V4, CapabilityAdvertisement.All)]
    public void OverviewPreservesMissingBindingAndCapabilityGates(ConsolePresentationMode presentation, CapabilityAdvertisement capabilities)
    {
        using var context = new BunitContext();
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = presentation == ConsolePresentationMode.V4 });
        context.Services.AddSingleton<IConsoleCapabilityManifest>(capabilities == CapabilityAdvertisement.All
            ? ConsoleCapabilityTestManifest.All
            : new ConsoleCapabilityManifest());
        context.Services.AddSingleton<IOperateTransitionDataSource>(new UnsupportedOperateTransitionDataSource());

        var page = context.Render<OperatePage>();

        page.WaitForAssertion(() => Assert.Contains("Missing binding", page.Markup));
        Assert.Equal("Operate", Assert.Single(page.FindAll("h1")).TextContent);
        Assert.Equal(presentation == ConsolePresentationMode.V4, page.FindAll(".console-operate-overview-v4").Count == 1);
        Assert.Equal(presentation == ConsolePresentationMode.V4, page.FindAll(".console-page-heading-v4").Count == 1);
        Assert.Equal(presentation == ConsolePresentationMode.V4 ? 1 : 0, page.FindAll("section > link[href='_content/Honua.Console.Shell/css/operate-overview-v4.css']").Count);
        if (presentation == ConsolePresentationMode.Legacy)
        {
            Assert.Contains("Operator workflow", page.Markup);
            Assert.Contains("control-plane settings live under Operate", page.Markup);
        }
        Assert.Single(page.FindAll("[data-ops-summary-strip]"));
        foreach (var route in new[] { "connections", "resources", "services", "publishing", "settings", "releases" })
            Assert.Single(page.FindAll($"a[href='/operate/{route}']"));
        foreach (var route in new[] { "temporal", "sync", "alerts/rules" })
            Assert.Equal(capabilities == CapabilityAdvertisement.All ? 1 : 0, page.FindAll($"a[href='/operate/{route}']").Count);
        Assert.Contains("Honua:Server:BaseUrl", page.Markup);
        Assert.DoesNotContain("County SFTP", page.Markup);
        if (presentation == ConsolePresentationMode.V4) CatalogAdminPreview.Capture(capabilities == CapabilityAdvertisement.All ? "operate-overview-capabilities" : "operate-overview-missing", page.Markup);
    }
}
