using Bunit;
using Honua.Console.Contracts;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.Layout;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Honua.Sdk.Admin.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class ConsoleUxWalkthroughTests
{
    [Fact]
    public void RootActionMenuAndContextMenuCreateFolderInPlaceAndRefreshTree()
    {
        using var context = new BunitContext();
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = true });
        var folders = new MutableFolderClient();
        var changes = new ConsoleFolderChanges();
        var source = new EmptyPublicationNavigationSource();
        context.Services.AddSingleton<IConsolePublicationNavigationSource>(source);
        context.Services.AddSingleton<IConsoleFolderClient>(folders);
        context.Services.AddSingleton(changes);

        var tree = context.Render<ConsolePublicationNavigation>();
        tree.WaitForAssertion(() => Assert.NotNull(tree.Find("[data-tree-action-trigger='environment']")));

        tree.Find("[data-tree-action-trigger='environment']").Click();
        var menu = tree.Find("[data-tree-context-menu='environment']");
        Assert.Equal("/operate/publishing", menu.QuerySelector("[data-tree-action='publish-service']")!.GetAttribute("href"));
        Assert.Equal("/operate/data/new", menu.QuerySelector("[data-tree-action='import-data']")!.GetAttribute("href"));
        Assert.Equal("/operate/services#folders", menu.QuerySelector("[data-tree-action='manage-folders']")!.GetAttribute("href"));

        menu.QuerySelector("[data-tree-action='new-folder']")!.Click();
        tree.Find("[data-tree-new-folder-name]").Input("utilities");
        tree.Find("[data-tree-new-folder-title]").Input("Utilities");
        tree.Find("[data-tree-new-folder] form").Submit();

        tree.WaitForAssertion(() => Assert.Single(tree.FindAll("[data-publication-key='folder:utilities']")));
        Assert.Empty(tree.FindAll("[data-tree-new-folder]"));
        Assert.Equal(1, folders.Creates);

        tree.Find("[data-publication-key='folder:utilities']").TriggerEvent("oncontextmenu", new MouseEventArgs());
        var folderMenu = tree.Find("[data-tree-context-menu='folder:utilities']");
        Assert.Equal("/operate/publishing?folder=utilities", folderMenu.QuerySelector("[data-tree-action='publish-in-folder']")!.GetAttribute("href"));
        Assert.Equal("/operate/services#folders", folderMenu.QuerySelector("[data-tree-action='manage-folder']")!.GetAttribute("href"));

        folderMenu.KeyDown(new KeyboardEventArgs { Key = "Escape" });
        Assert.Empty(tree.FindAll("[data-tree-context-menu]"));
    }

    [Fact]
    public void PublishingWizardPreselectsFolderFromQueryParameter()
    {
        using var ctx = new BunitContext();
        ctx.AddConsoleNotifications();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IOperateTransitionDataSource>(new FixedServicesDataSource());
        ctx.Services.AddSingleton<IServiceLayerPublishOperation>(new UnsupportedServiceLayerPublishOperation());
        ctx.Services.AddSingleton<IConsoleFolderClient>(new MutableFolderClient(["utilities"]));

        var wizard = ctx.Render<PublishWizardWorkspace>(p => p.Add(c => c.InitialFolderId, "utilities"));

        wizard.WaitForAssertion(() => Assert.NotNull(wizard.Find("[data-new-service]")));
        Assert.Equal("folder:utilities", wizard.Find("[data-publish-folder]").GetAttribute("value"));
    }

    [Fact]
    public async Task OptInDisabledCapabilitiesShowPreviewOffBadgesAndGuidance()
    {
        var manifest = new ManifestBackedConsoleCapabilityManifest(new StaticRegistry(new CapabilityRegistrySnapshot
        {
            Bound = true,
            Descriptors =
            [
                new CapabilityDescriptor("alerts.geofence", Available: false, Supported: true, ReasonCode: "opt-in-disabled", OptInRequired: true, Lifecycle: "Preview"),
                new CapabilityDescriptor("gitops.release-manifest", Available: false, Supported: true, ReasonCode: "opt-in-disabled", OptInRequired: true, Lifecycle: "Preview"),
            ]
        }));
        await manifest.RefreshAsync();

        Assert.False(manifest.IsAdvertised(ConsoleCapabilityKeys.RealtimeAlerting));
        Assert.True(manifest.IsOptInDisabled(ConsoleCapabilityKeys.RealtimeAlerting));
        Assert.True(manifest.IsOptInDisabled(ConsoleCapabilityKeys.CrossEnvironmentPromotion));

        await using var ctx = new BunitContext();
        ctx.Services.AddSingleton<IConsoleCapabilityManifest>(manifest);
        ctx.Services.AddSingleton<IOperateAlertRulesDataSource, UnsupportedOperateAlertRulesDataSource>();
        ctx.Services.AddSingleton<IConsoleAlertInboxClient>(new StubAlertInboxClient());
        ctx.Services.AddSingleton(new ConsoleV4Options { Enabled = true });

        var nav = ctx.Render<ConsoleTopNavigation>(parameters => parameters.Add(p => p.Groups, ConsoleNavigation.Groups));
        var releasesLink = nav.Find("a[href='/operate/releases']");
        Assert.Equal("opt-in-off", releasesLink.GetAttribute("data-capability-state"));
        Assert.Contains("Preview · Off", releasesLink.TextContent, StringComparison.Ordinal);

        var alertsPage = ctx.Render<OperateAlertRulesPage>();
        Assert.Contains("Preview feature (off by default)", alertsPage.Markup, StringComparison.Ordinal);
        Assert.Contains("alerts.geofence", alertsPage.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Unsupported by this server", alertsPage.Markup, StringComparison.Ordinal);

        var alertsPanel = ctx.Render<OperationsAlertsPanel>();
        alertsPanel.WaitForAssertion(() => Assert.Contains("Preview feature (off by default)", alertsPanel.Markup, StringComparison.Ordinal));
        Assert.Contains("Alerts are not enabled or available on this server.", alertsPanel.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Unsupported by this server", alertsPanel.Markup, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("GPServer", "Processes / Tasks", "Geoprocessing service", "process")]
    [InlineData("GeocodeServer", "Locators", "Geocoding service", "service")]
    [InlineData("GeometryServer", "Operations", "Geometry service", "service")]
    public void NonLayerServicesRenderOperationsInsteadOfLayersInTreeDirectoryAndDetail(
        string protocol, string expectedTab, string expectedSummary, string expectedIcon)
    {
        var tree = new ConsolePublicationTree();
        tree.Load(new HonuaAdminServicePage
        {
            Items = [new ServiceSummary { ServiceName = "toolbox", EnabledProtocols = [protocol], LayerCount = 0 }],
            Page = 1,
            PageSize = 100,
            Total = 1,
            LayerTotal = 0
        }, "Environment");

        var node = Assert.Single(tree.Root.Children);
        Assert.Null(node.ChildCount);
        Assert.False(node.HasChildren);
        Assert.Equal(expectedIcon, ConsoleTreeIcon.ForNode(node));

        var detail = new OperateServiceDetail(
            "toolbox", "Spatial Toolbox", protocol, "Configured", "honua-server",
            [], [], [new OperateServiceEndpoint(protocol, $"https://server.example.test/rest/services/toolbox/{protocol}", "Exposed")]);

        using var ctx = new BunitContext();
        ctx.Services.AddSingleton<NavigationManager>(new MountedNavigationManager("/console/"));
        ctx.Services.AddSingleton(new ConsoleV4Options { Enabled = true });
        ctx.Services.AddSingleton<ConsoleAgentContextRegistry>();
        ctx.AddConsoleNotifications();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IOperateTransitionDataSource>(new FixedServicesDataSource(detail));
        ctx.Services.AddSingleton<IServiceConfigurationOperation>(new UnsupportedServiceConfigurationOperation());

        var directory = ctx.Render<OperateServicesPage>();
        Assert.Equal(expectedTab, directory.FindAll("tbody td")[2].TextContent.Trim());

        var servicePage = ctx.Render<OperateServiceDetailPage>(p => p.Add(c => c.ServiceName, detail.Name));
        servicePage.WaitForAssertion(() => Assert.NotNull(servicePage.Find("[data-service-summary]")));

        Assert.Contains(expectedSummary, servicePage.Find("[data-service-summary]").TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("0 layers", servicePage.Find("[data-service-summary]").TextContent, StringComparison.Ordinal);
        Assert.Equal(expectedTab, servicePage.Find("#service-layers-tab").TextContent.Trim());
        Assert.Empty(servicePage.FindAll("[data-service-add-layer]"));
        Assert.Single(servicePage.FindAll("[data-service-operations]"));
        if (protocol == "GPServer")
        {
            Assert.Equal("/console/operate/geoprocessing", servicePage.Find("[data-service-gp-jobs]").GetAttribute("href"));
            Assert.Equal("/console/studio/processes", servicePage.Find("[data-service-gp-processes]").GetAttribute("href"));
        }
    }

    private sealed class EmptyPublicationNavigationSource : IConsolePublicationNavigationSource
    {
        public Task<OperateSectionResult<HonuaAdminServicePage>> ReadServicesAsync(
            HonuaAdminCatalogQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<HonuaAdminServicePage>.Allowed(new HonuaAdminServicePage
            {
                Items = [],
                Page = 1,
                PageSize = 100,
                Total = 0,
                LayerTotal = 0
            }));

        public Task<OperateSectionResult<IReadOnlyList<string>>> ReadConnectionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<IReadOnlyList<string>>.Allowed([]));

        public Task<OperateSectionResult<IReadOnlyList<ConsolePublicationLayer>>> ReadLayersAsync(
            string serviceName, IReadOnlyList<string> connections, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<IReadOnlyList<ConsolePublicationLayer>>.Allowed([]));
    }

    private sealed class MutableFolderClient : IConsoleFolderClient
    {
        private readonly List<Folder> _folders;
        public int Creates { get; private set; }

        public MutableFolderClient(IEnumerable<string>? initialFolderNames = null)
        {
            _folders = (initialFolderNames ?? [])
                .Select(name => new Folder { Metadata = new() { Id = name, Name = name } })
                .ToList();
        }

        public Task<OperateSectionResult<ConsoleFolderCatalog>> ReadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<ConsoleFolderCatalog>.Allowed(new([.. _folders], "\"etag-1\"", "authority")));

        public Task<ConsoleFolderMutationResult> CreateAsync(ConsoleFolderCatalog catalog, CreateFolderRequest request, CancellationToken cancellationToken = default)
        {
            Creates++;
            _folders.Add(new Folder { Metadata = new() { Id = request.Name, Name = request.Name, Title = request.Title } });
            return Task.FromResult(new ConsoleFolderMutationResult { Succeeded = true, State = "Saved" });
        }

        public Task<ConsoleFolderMutationResult> UpdateAsync(ConsoleFolderCatalog catalog, string folderId, UpdateFolderRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ConsoleFolderMutationResult> DeleteAsync(ConsoleFolderCatalog catalog, string folderId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ConsoleFolderMutationResult> MoveAsync(ConsoleFolderCatalog catalog, string serviceName, MoveServiceRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class StaticRegistry(CapabilityRegistrySnapshot snapshot) : ICapabilityRegistryClient
    {
        public Task<CapabilityRegistrySnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(snapshot);
    }

    private sealed class StubAlertInboxClient : IConsoleAlertInboxClient
    {
        public Task<OperateSectionResult<ConsoleAlertInboxSnapshot>> ListAlertsAsync(
            CancellationToken cancellationToken = default, string? cursor = null, ConsoleAlertAuthority? expectedAuthority = null) =>
            throw new InvalidOperationException("Must not query alert inbox when alerting is not advertised.");

        public Task<OperateSectionResult<ObservabilityAlertEventResponse>> AcknowledgeAlertAsync(
            ConsoleAlertAcknowledgement command, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<OperateSectionResult<ObservabilityAlertEventResponse>> ChangeAlertLifecycleAsync(
            ConsoleAlertLifecycleCommand command, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
