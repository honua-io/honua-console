using Bunit;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.DependencyInjection;
using Honua.Console.Shell.Layout;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class ConsoleNavigationRenderTests
{
    [Theory]
    [InlineData(ConsolePresentationMode.Legacy)]
    [InlineData(ConsolePresentationMode.V4)]
    public async Task HeaderKeepsNavigationTargetsAcrossTheV4Flag(ConsolePresentationMode mode)
    {
        var enabled = mode == ConsolePresentationMode.V4;
        await using var context = CreateContext(shellOptions: new HonuaConsoleShellOptions { CatalogAdminEnabled = true });
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = enabled });
        context.Services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Studio:EmbedJsStudio"] = "true" }).Build());
        var navigation = context.Render<ConsoleLayout>();
        Assert.Equal(enabled ? 0 : 1, navigation.FindAll(".console-topbar-brand > span").Count);
        Assert.Equal("Honua Console", navigation.Find(".console-topbar-brand strong").TextContent);
        Assert.Equal("/", navigation.Find(".console-topbar-brand").GetAttribute("href"));
        Assert.Equal("/environments", navigation.Find("[data-workspace-header-target]").GetAttribute("href"));
        Assert.Equal("/support", navigation.Find(".console-help-link").GetAttribute("href"));
        Assert.Equal("console-conversation-drawer", navigation.Find("[data-conversation-toggle]").GetAttribute("aria-controls"));
        Assert.Equal(enabled ? 2 : 0, navigation.FindAll(".console-help-link svg, .console-conversation-toggle svg").Count);
        Assert.Equal(["Data", "Studio", "Catalogs", "Operate", "Access", "Settings"],
            navigation.FindAll(".console-topnav [data-navigation-menu]").Select(element => element.GetAttribute("data-navigation-menu")));
        var heading = context.Render<ConsolePageHeading>(parameters => parameters.Add(component => component.Title, "Home"));
        var panel = context.Render<ConsolePanel>(parameters => parameters
            .Add(component => component.Title, "Services")
            .Add(component => component.HeadingId, "services-heading"));
        var conversation = navigation.Find("[data-conversation-toggle]");
        var help = navigation.Find(".console-help-link");
        Assert.Equal(enabled ? 1 : 0, navigation.FindAll("[data-console-theme-toggle]").Count);
        if (enabled)
        {
            var theme = navigation.Find("[data-console-theme-toggle]");
            Assert.Equal("button", theme.GetAttribute("type"));
            Assert.Equal("false", theme.GetAttribute("aria-pressed"));
            Assert.Equal("Switch to dark mode", theme.GetAttribute("aria-label"));
        }
        Assert.Equal(enabled ? "Conversation" : null, conversation.GetAttribute("aria-label"));
        Assert.Equal(enabled ? "Help" : null, help.GetAttribute("aria-label"));
        if (!enabled)
        {
            Assert.Equal("Conversation", conversation.TextContent.Trim());
            Assert.Equal("Help", help.TextContent.Trim());
            Assert.Empty(conversation.Children);
            Assert.Empty(help.Children);
        }
        var flag = enabled.ToString().ToLowerInvariant();
        CatalogAdminPreview.Capture($"navigation-v4-{flag}", $"<div class=\"console-shell\" data-console-v4=\"{flag}\">{navigation.Find(".console-topbar").OuterHtml}<main id=\"console-main\" class=\"console-main\" tabindex=\"-1\">{heading.Markup}<h2>Workspace</h2>{panel.Markup}</main></div>");
    }

    [Theory]
    [InlineData(true, "Content")]
    [InlineData(false, "Catalog")]
    public async Task ProgrammaticCatalogOptionsControlNavigationAndRecentLabels(bool enabled, string label)
    {
        await using var context = CreateContext(shellOptions: new HonuaConsoleShellOptions { CatalogAdminEnabled = enabled });
        context.Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Honua:Console:Catalogs:Enabled"] = enabled ? "false" : "true"
        }).Build());
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("/catalog");
        var shell = context.Render<ConsoleLayout>();
        shell.WaitForAssertion(() => Assert.Equal(label, shell.Find(".console-contextbar summary").TextContent));
        Assert.Equal(label, shell.Find("[data-console-recent-label]").GetAttribute("data-console-recent-label"));
        Assert.Equal(enabled ? 1 : 0, shell.FindAll("[data-navigation-menu='Catalogs']").Count);
        var workAreas = context.Render<ConsoleHomeWorkAreas>();
        Assert.Equal(enabled ? 1 : 0, workAreas.FindAll("a[href='/catalogs']").Count);
    }

    [Theory]
    [InlineData(true, "Content")]
    [InlineData(false, "Catalog")]
    public async Task NestedContentBreadcrumbUsesCatalogOptionsWithoutReadAccess(bool enabled, string label)
    {
        await using var context = CreateContext(shellOptions: new HonuaConsoleShellOptions { CatalogAdminEnabled = enabled });
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("/catalog/item-1");
        var shell = context.Render<ConsoleLayout>();
        Assert.Equal(label, shell.Find(".console-contextbar ol > li > a[href='/catalog']").TextContent);
        Assert.Equal("item-1", shell.Find(".console-contextbar summary").TextContent);
        Assert.Empty(shell.FindAll(".console-topnav a[href='/catalog']"));
    }

    [Theory]
    [InlineData(ConsoleProductMode.Full, true)]
    [InlineData(ConsoleProductMode.Witness, false)]
    public async Task RegroupedDiscoveryRetainsItsModeGate(ConsoleProductMode mode, bool visible)
    {
        await using var context = CreateContext(mode, new HonuaConsoleShellOptions { CatalogAdminEnabled = true });
        var shell = context.Render<ConsoleLayout>();
        Assert.Equal(visible ? 1 : 0, shell.FindAll(".console-topnav a[href='/operate/catalogs']").Count);
        Assert.Empty(shell.FindAll(".console-topnav a[href='/share/manage']"));
    }

    [Fact]
    public async Task CatalogAccessEntryFollowsCapabilityRevocation()
    {
        await using var context = CreateContext(shellOptions: new HonuaConsoleShellOptions { CatalogAdminEnabled = true });
        var manifest = new MutableCatalogManifest();
        manifest.AdvertiseCatalog();
        context.Services.AddSingleton<IConsoleCapabilityManifest>(manifest);
        var shell = context.Render<ConsoleLayout>();
        Assert.Single(shell.FindAll("[data-navigation-menu='Catalogs'] a[href='/share/manage']"));
        manifest.RevokeCatalog();
        shell.WaitForAssertion(() => Assert.Empty(shell.FindAll(".console-topnav a[href='/share/manage']")));
    }

    [Theory]
    [InlineData(true, "/catalog", "Content")]
    [InlineData(true, "/catalogs", "Catalogs")]
    [InlineData(true, "/operate/catalogs", "Discovery endpoints")]
    [InlineData(false, "/catalog", "Catalog")]
    [InlineData(false, "/operate/catalogs", "Discovery catalogs")]
    public void ContextAndRecentLabelsUseTheActiveNavigation(bool enabled, string path, string label)
    {
        using var context = CreateContext();
        context.Services.AddSingleton<IConsoleCapabilityManifest>(new ConsoleCapabilityManifest([]));
        context.Services.AddSingleton(new CatalogAdminOptions { Enabled = enabled });
        context.Services.GetRequiredService<NavigationManager>().NavigateTo(path);
        var shell = context.Render<ConsoleLayout>();
        shell.WaitForAssertion(() => Assert.Equal(label, shell.Find(".console-contextbar summary").TextContent));
        shell.WaitForAssertion(() => Assert.Equal(label, shell.Find("[data-console-recent-label]").GetAttribute("data-console-recent-label")));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CatalogLabelsSurviveCapabilityRevocation(bool enabled)
    {
        using var context = CreateContext();
        var manifest = new MutableCatalogManifest();
        manifest.AdvertiseCatalog();
        context.Services.AddSingleton<IConsoleCapabilityManifest>(manifest);
        context.Services.AddSingleton(new CatalogAdminOptions { Enabled = enabled });
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("/catalog");
        var shell = context.Render<ConsoleLayout>();
        manifest.RevokeCatalog();
        shell.WaitForAssertion(() => Assert.Empty(shell.FindAll(".console-topnav a[href='/catalog']")));
        Assert.Equal(enabled ? "Content" : "Catalog", shell.Find(".console-contextbar summary").TextContent);
        Assert.Equal(enabled ? "Content" : "Catalog", shell.Find("[data-console-recent-label]").GetAttribute("data-console-recent-label"));
    }

    [Fact]
    public void CatalogPreviewGroupsAdministrationAndNamesTheContentLibrary()
    {
        var groups = ConsoleNavigation.VisibleGroups(new ConfiguredConsoleProductMode(ConsoleProductMode.Full),
            new ConsoleCapabilityManifest([ConsoleCapabilityKeys.PersistentCatalog]),
            ConsoleNavigation.Definitions(new CatalogAdminOptions { Enabled = true }));
        Assert.Equal(["Data", "Studio", "Catalogs", "Operate", "Access", "Settings"], groups.Select(group => group.Name));
        Assert.Equal("Content", groups.SelectMany(group => group.Destinations).First(entry => entry.Path == "/catalog").Label);
        Assert.Contains(groups.Single(group => group.Name == "Catalogs").Destinations, entry => entry.Path == "/operate/catalogs");
        var original = ConsoleNavigation.Groups.SelectMany(group => group.Destinations).Single(entry => entry.Path == "/operate/catalogs");
        Assert.Contains(original with { Label = "Discovery endpoints" }, groups.Single(group => group.Name == "Catalogs").Destinations);
    }

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
        CatalogAdminPreview.Capture("navigation", navigation.Find(".console-topbar").OuterHtml);
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

    [Theory]
    [InlineData(ConsolePresentationMode.Legacy)]
    [InlineData(ConsolePresentationMode.V4)]
    public void WitnessKeepsInspectionAndEnvironmentIdentityWithoutAdminMenus(ConsolePresentationMode mode)
    {
        var enabled = mode == ConsolePresentationMode.V4;
        using var context = CreateContext(ConsoleProductMode.Witness);
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = enabled });
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

    [Theory]
    [InlineData(ConsolePresentationMode.Legacy)]
    [InlineData(ConsolePresentationMode.V4)]
    public void MountedNavigationReflectsCapabilityAvailabilityAndRevocation(ConsolePresentationMode mode)
    {
        var enabled = mode == ConsolePresentationMode.V4;
        using var context = CreateContext();
        var manifest = new MutableCatalogManifest();
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = enabled });
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

    private static BunitContext CreateContext(ConsoleProductMode mode = ConsoleProductMode.Full, HonuaConsoleShellOptions? shellOptions = null)
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.AddConsoleNotifications();
        context.AddAuthorization().SetAuthorized("synthetic-operator");
        context.Services.AddSingleton<IConsoleProductMode>(new ConfiguredConsoleProductMode(mode));
        context.Services.AddSingleton<IConsoleCapabilityManifest>(ConsoleCapabilityManifest.FromConfigurationList(null));
        if (shellOptions is null) context.Services.AddSingleton(new CatalogAdminOptions());
        else context.Services.AddHonuaConsoleShell(shellOptions);
        return context;
    }
}
