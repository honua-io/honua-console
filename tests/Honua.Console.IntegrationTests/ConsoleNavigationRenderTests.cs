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
        var navigation = context.Render<ConsoleLayout>();
        Assert.Equal(["Data", "Protocols", "Studio", "Operate", "Access", "Settings"],
            navigation.FindAll(".console-topnav [data-navigation-menu]").Select(element => element.GetAttribute("data-navigation-menu")));
        Assert.Equal("/studio/processes", navigation.Find("a[href='/studio/processes']").GetAttribute("href"));
        Assert.Equal(["/studio", "/studio/processes", "/studio/form", "/studio/report"],
            navigation.FindAll("[data-navigation-menu='Studio'] a").Select(element => element.GetAttribute("href")));
        Assert.Empty(navigation.FindAll("a[href='/studio'] small, a[href='/studio/processes'] small"));
        Assert.Single(navigation.FindAll("[data-navigation-menu='Protocols'] a"));
        Assert.True(navigation.Find("[data-tree-filter]").HasAttribute("disabled"));
        Assert.Empty(navigation.FindAll("[data-navigation-menu='Studio'] a[href='/maps/new']"));
        Assert.Equal("/operate/catalogs", navigation.Find("[data-navigation-menu='Protocols'] a").GetAttribute("href"));
        Assert.Empty(navigation.FindAll("a[href='/operate/license'], a[href='/operate/server-info']"));
    }

    [Fact]
    public void CatalogMapEntryNamesItsRequiredSourceAndFollowsCatalogAvailability()
    {
        using var context = CreateContext();
        context.Services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Studio:EmbedJsStudio"] = "true" }).Build());
        var manifest = new MutableCatalogManifest();
        context.Services.AddSingleton<IConsoleCapabilityManifest>(manifest);
        manifest.AdvertiseCatalog();
        var navigation = context.Render<ConsoleLayout>();
        var entry = navigation.Find("[data-navigation-menu='Studio'] a[href='/catalog']");
        Assert.Contains("Map from catalog", entry.TextContent);
        Assert.Empty(navigation.FindAll("a[href='/maps/new']"));
        manifest.RevokeCatalog();
        navigation.WaitForAssertion(() => Assert.Empty(navigation.FindAll("[data-navigation-menu='Studio'] a[href='/catalog']")));
    }

    [Fact]
    public void UnadvertisedCatalogAndDisabledStudioDoNotBecomeNavigationEntries()
    {
        using var context = CreateContext();
        var navigation = context.Render<ConsoleLayout>();
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
        var breadcrumbs = context.Render<ConsoleBreadcrumbs>(parameters => parameters.Add(p => p.Groups, ConsoleNavigation.Groups));
        Assert.Equal("public roads", breadcrumbs.Find("summary").TextContent);
        Assert.Equal("/operate/services/public%20roads/settings",
            breadcrumbs.Find("a[href*='/services/']").GetAttribute("href"));
        Assert.Equal("/operate/data/new", breadcrumbs.Find("a[href='/operate/data/new']").GetAttribute("href"));
        Assert.Single(breadcrumbs.FindAll("a[href='/studio']"));
        manager.NavigateTo("/operate/data");
        breadcrumbs.WaitForAssertion(() => Assert.Equal("Datasets", breadcrumbs.Find("summary").TextContent));
        Assert.Empty(breadcrumbs.FindAll("a[href*='/services/']"));
    }

    [Fact]
    public void UnreadableApprovalQueueDoesNotDisplayAnInventedZero()
    {
        using var context = CreateContext();
        var inbox = new DeferredApprovalInboxClient();
        context.Services.AddSingleton<IConsoleApprovalInboxClient>(inbox);
        var count = context.Render<ConsoleApprovalCount>();
        Assert.Empty(count.FindAll(".console-approval-count"));
        var initialRenderCount = count.RenderCount;
        inbox.Response.SetResult(OperateSectionResult<ApprovalInboxSnapshot>.Denied(OperateSectionStatus.Forbidden, "Access denied."));
        count.WaitForState(() => count.RenderCount > initialRenderCount);
        Assert.Empty(count.FindAll(".console-approval-count"));
    }

    [Fact]
    public void ApprovalBadgeStartsRealtimeWhileItsInitialReadIsPending()
    {
        using var context = CreateContext();
        var inbox = new DeferredApprovalInboxClient();
        var realtime = new FakeConsoleProposalRealtimeClient();
        context.Services.AddSingleton<IConsoleApprovalInboxClient>(inbox);
        context.Services.AddSingleton<IConsoleProposalRealtimeClient>(realtime);
        var count = context.Render<ConsoleApprovalCount>();
        Assert.Equal(1, realtime.StartCount);
        Assert.True(realtime.IsConnected);
        Assert.Empty(count.FindAll(".console-approval-count"));
        inbox.Response.SetResult(OperateSectionResult<ApprovalInboxSnapshot>.Allowed(ApprovalInboxSnapshot.Empty));
        count.WaitForAssertion(() => Assert.Equal("0", count.Find(".console-approval-count").TextContent));
    }

    [Fact]
    public void ApprovalBadgeRefreshesPendingAndResolvedEventsWithoutNavigation()
    {
        using var context = CreateContext();
        var inbox = new ScriptedApprovalInboxClient();
        var realtime = new FakeConsoleProposalRealtimeClient();
        context.Services.AddSingleton<IConsoleApprovalInboxClient>(inbox);
        context.Services.AddSingleton<IConsoleProposalRealtimeClient>(realtime);
        var count = context.Render<ConsoleApprovalCount>();
        count.WaitForAssertion(() => Assert.Equal("0", count.Find(".console-approval-count").TextContent));
        Assert.True(realtime.IsConnected);
        inbox.Result = OperateSectionResult<ApprovalInboxSnapshot>.Allowed(
            FakeProposalFactory.Snapshot(FakeProposalFactory.Summary("pending", ConsoleProposalKind.Deploy)));
        realtime.Raise(new ConsoleProposalEvent(ConsoleProposalEventKind.Pending, "pending", ConsoleProposalKind.Deploy,
            ConsoleProposalStatus.AwaitingApproval, null, ConsoleProposalRisk.Low, DateTimeOffset.UtcNow));
        count.WaitForAssertion(() => Assert.Equal("1", count.Find(".console-approval-count").TextContent));
        inbox.Result = OperateSectionResult<ApprovalInboxSnapshot>.Allowed(ApprovalInboxSnapshot.Empty);
        realtime.Raise(new ConsoleProposalEvent(ConsoleProposalEventKind.Resolved, "pending", ConsoleProposalKind.Deploy,
            ConsoleProposalStatus.Submitted, null, ConsoleProposalRisk.Low, DateTimeOffset.UtcNow));
        count.WaitForAssertion(() => Assert.Equal("0", count.Find(".console-approval-count").TextContent));
    }

    [Fact]
    public void WitnessKeepsInspectionAndEnvironmentIdentityWithoutAdminMenus()
    {
        using var context = CreateContext(ConsoleProductMode.Witness);
        context.Services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Studio:EmbedJsStudio"] = "true" }).Build());
        var navigation = context.Render<ConsoleLayout>();
        Assert.Empty(navigation.FindAll("[data-navigation-menu='Protocols'], [data-navigation-menu='Access'], [data-navigation-menu='Settings']"));
        Assert.Empty(navigation.FindAll(".console-topnav a[href='/operate/copilot'], .console-topnav a[href='/maps/new'], .console-topnav a[href='/studio/styles']"));
        Assert.Equal(["/studio", "/studio/processes"], navigation.FindAll("[data-navigation-menu='Studio'] a")
            .Select(element => element.GetAttribute("href")));
        Assert.Single(navigation.FindAll("[data-workspace-header-target]"));
        Assert.NotEmpty(navigation.FindAll("a[href='/inbox'], a[href='/operate/releases']"));
    }

    [Fact]
    public void MountedNavigationReflectsCapabilityAvailabilityAndRevocation()
    {
        using var context = CreateContext();
        var manifest = new MutableCatalogManifest();
        context.Services.AddSingleton<IConsoleCapabilityManifest>(manifest);
        var navigation = context.Render<ConsoleLayout>();
        Assert.Empty(navigation.FindAll(".console-topnav a[href='/catalog'], .console-topnav a[href='/share/public']"));
        manifest.AdvertiseCatalog();
        navigation.WaitForAssertion(() => Assert.Equal(2,
            navigation.FindAll(".console-topnav a[href='/catalog'], .console-topnav a[href='/share/public']").Count));
        manifest.RevokeCatalog();
        navigation.WaitForAssertion(() => Assert.Empty(
            navigation.FindAll(".console-topnav a[href='/catalog'], .console-topnav a[href='/share/public']")));
    }

    [Theory]
    [InlineData("/operate/health", "Health")]
    [InlineData("/operate/layers", "Layers")]
    [InlineData("/operate/layers/resource-42", "resource-42")]
    [InlineData("/studio/processes", "Processes")]
    [InlineData("/operate", "Dashboard")]
    public void BreadcrumbUsesTheDestinationOrOpaqueObjectLabel(string route, string expected)
    {
        using var context = CreateContext();
        context.Services.GetRequiredService<NavigationManager>().NavigateTo(route);
        var breadcrumbs = context.Render<ConsoleBreadcrumbs>(parameters => parameters.Add(p => p.Groups, ConsoleNavigation.Groups));
        Assert.Equal(expected, breadcrumbs.Find("summary").TextContent);
        Assert.NotEmpty(breadcrumbs.FindAll("[role='group'] a"));
    }

    private sealed class MutableCatalogManifest : IConsoleCapabilityManifest
    {
        private bool _available;
        public event Action? Changed;
        public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public bool IsAdvertised(string capabilityKey) => _available && capabilityKey == ConsoleCapabilityKeys.PersistentCatalog;
        public void AdvertiseCatalog()
        {
            _available = true;
            Changed?.Invoke();
        }

        public void RevokeCatalog()
        {
            _available = false;
            Changed?.Invoke();
        }
    }

    private sealed class DeferredApprovalInboxClient : IConsoleApprovalInboxClient
    {
        public TaskCompletionSource<OperateSectionResult<ApprovalInboxSnapshot>> Response { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<OperateSectionResult<ApprovalInboxSnapshot>> GetInboxAsync(
            string? status = null, string? kind = null, CancellationToken cancellationToken = default) =>
            Response.Task.WaitAsync(cancellationToken);
    }

    private static BunitContext CreateContext(ConsoleProductMode mode = ConsoleProductMode.Full)
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.AddConsoleNotifications();
        context.AddAuthorization().SetAuthorized("synthetic-operator");
        context.Services.AddSingleton<IConsoleProductMode>(new ConfiguredConsoleProductMode(mode));
        context.Services.AddSingleton<IConsoleCapabilityManifest>(ConsoleCapabilityManifest.FromConfigurationList(null));
        return context;
    }
}
