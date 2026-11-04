using Bunit;
using Honua.Console.Shell.Layout;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class ConsoleNavigationRenderTests
{
    [Fact]
    public void NavigationUsesExistingDestinationsAndKeepsProcessesDiscoverable()
    {
        using var context = CreateContext();
        context.Services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Studio:EmbedJsStudio"] = "true" }).Build());
        var navigation = context.Render<ConsoleTopNavigation>();
        Assert.Equal(["Data", "Protocols", "Studio", "Operate", "Access", "Settings"],
            navigation.FindAll("[data-navigation-menu]").Select(element => element.GetAttribute("data-navigation-menu")));
        Assert.Equal("/studio/processes", navigation.Find("a[href='/studio/processes']").GetAttribute("href"));
        Assert.Single(navigation.FindAll("[data-navigation-menu='Protocols'] a"));
        Assert.Equal("/operate/catalogs", navigation.Find("[data-navigation-menu='Protocols'] a").GetAttribute("href"));
        Assert.Empty(navigation.FindAll("input[type='search'], a[href='/operate/license'], a[href='/operate/server-info']"));
    }

    [Fact]
    public void UnadvertisedCatalogAndDisabledStudioDoNotBecomeNavigationEntries()
    {
        using var context = CreateContext();
        var navigation = context.Render<ConsoleTopNavigation>();
        Assert.Empty(navigation.FindAll("a[href='/catalog'], a[href='/share/public'], [data-navigation-menu='Studio']"));
        Assert.Equal("false", navigation.Find("[data-navigation-toggle]").GetAttribute("aria-expanded"));
        Assert.Equal(navigation.Find("nav[aria-label='Primary']").Id,
            navigation.Find("[data-navigation-toggle]").GetAttribute("aria-controls"));
    }

    [Fact]
    public void ServiceBreadcrumbRetainsOpaqueServiceRouteWithoutConnectionAncestor()
    {
        using var context = CreateContext();
        var manager = context.Services.GetRequiredService<NavigationManager>();
        manager.NavigateTo("/operate/services/public%20roads/settings?tab=access");
        var breadcrumbs = context.Render<ConsoleBreadcrumbs>();
        Assert.Equal("public roads", breadcrumbs.Find("summary").TextContent);
        Assert.Equal("/operate/services/public%20roads/settings",
            breadcrumbs.Find("a[href*='/services/']").GetAttribute("href"));
        manager.NavigateTo("/operate/data");
        breadcrumbs.WaitForAssertion(() => Assert.Equal("Data & Layers", breadcrumbs.Find("summary").TextContent));
        Assert.Empty(breadcrumbs.FindAll("a[href*='/services/']"));
    }

    [Fact]
    public void UnreadableApprovalQueueDoesNotDisplayAnInventedZero()
    {
        using var context = CreateContext();
        context.Services.AddSingleton<IConsoleApprovalInboxClient>(new ScriptedApprovalInboxClient
        {
            Result = OperateSectionResult<ApprovalInboxSnapshot>.Denied(OperateSectionStatus.Forbidden, "Access denied.")
        });
        var count = context.Render<ConsoleApprovalCount>();
        Assert.Empty(count.FindAll(".console-approval-count"));
    }

    private static BunitContext CreateContext()
    {
        var context = new BunitContext();
        context.Services.AddSingleton<IConsoleProductMode>(new ConfiguredConsoleProductMode(ConsoleProductMode.Full));
        context.Services.AddSingleton<IConsoleCapabilityManifest>(ConsoleCapabilityManifest.FromConfigurationList(null));
        return context;
    }
}
