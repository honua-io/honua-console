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
        context.Services.AddSingleton<IConsolePublicationNavigationSource>(new EmptyPublicationNavigationSource());
        context.Services.AddSingleton<IConsoleFolderClient>(folders);
        context.Services.AddSingleton(new ConsoleFolderChanges());

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
        VerifyFolderContextMenu(tree);
    }

    [Fact]
    public async Task DismissingNewFolderDialogDuringSlowReadSkipsFolderCreation()
    {
        using var context = new BunitContext();
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = true });
        var pendingRead = new TaskCompletionSource<OperateSectionResult<ConsoleFolderCatalog>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var folders = new MutableFolderClient { PendingRead = pendingRead };
        context.Services.AddSingleton<IConsolePublicationNavigationSource>(new EmptyPublicationNavigationSource());
        context.Services.AddSingleton<IConsoleFolderClient>(folders);

        var menu = context.Render<ConsolePublicationNodeMenu>(p => p
            .Add(c => c.NewFolderOpen, true));
        menu.Find("[data-tree-new-folder-name]").Input("utilities");
        var submission = menu.Find("[data-tree-new-folder] form").SubmitAsync();
        menu.Find("[data-tree-new-folder-cancel]").Click();

        pendingRead.TrySetResult(OperateSectionResult<ConsoleFolderCatalog>.Allowed(new([], "\"etag-1\"", "authority")));
        await submission;

        Assert.Equal(0, folders.Creates);
    }

    private static void VerifyFolderContextMenu(IRenderedComponent<ConsolePublicationNavigation> tree)
    {
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
        var manifest = await CreateOptInDisabledManifestAsync();
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
    [InlineData("GPServer", "process")]
    [InlineData("GeocodeServer", "service")]
    [InlineData("GeometryServer", "service")]
    public void NonLayerServicesSuppressLayerCountsAndExpansionInTree(string protocol, string expectedIcon)
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
    }

    [Theory]
    [InlineData("GPServer", "Processes / Tasks", "Geoprocessing service")]
    [InlineData("GeocodeServer", "Locators", "Geocoding service")]
    [InlineData("GeometryServer", "Operations", "Geometry service")]
    public void NonLayerServicesRenderOperationsInsteadOfLayersInDirectoryAndDetail(
        string protocol, string expectedTab, string expectedSummary)
    {
        var detail = new OperateServiceDetail(
            "toolbox", "Spatial Toolbox", protocol, "Configured", "honua-server",
            [], [], [new OperateServiceEndpoint(protocol, $"https://server.example.test/rest/services/toolbox/{protocol}", "Exposed")]);

        using var ctx = CreateServiceContext(detail);
        var directory = ctx.Render<OperateServicesPage>();
        Assert.Equal(expectedTab, directory.FindAll("tbody td")[2].TextContent.Trim());

        var servicePage = ctx.Render<OperateServiceDetailPage>(p => p.Add(c => c.ServiceName, detail.Name));
        servicePage.WaitForAssertion(() => Assert.NotNull(servicePage.Find("[data-service-summary]")));
        Assert.Contains(expectedSummary, servicePage.Find("[data-service-summary]").TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("0 layers", servicePage.Find("[data-service-summary]").TextContent, StringComparison.Ordinal);
        Assert.Equal(expectedTab, servicePage.Find("#service-layers-tab").TextContent.Trim());
        Assert.Empty(servicePage.FindAll("[data-service-add-layer]"));
        Assert.Single(servicePage.FindAll("[data-service-operations]"));
    }

    [Fact]
    public void GeoprocessingServiceDetailLinksToGeoprocessingJobsAndProcesses()
    {
        var detail = new OperateServiceDetail(
            "toolbox", "Spatial Toolbox", "GPServer", "Configured", "honua-server",
            [], [], [new OperateServiceEndpoint("GPServer", "https://server.example.test/rest/services/toolbox/GPServer", "Exposed")]);

        using var ctx = CreateServiceContext(detail);
        var servicePage = ctx.Render<OperateServiceDetailPage>(p => p.Add(c => c.ServiceName, detail.Name));
        servicePage.WaitForAssertion(() => Assert.NotNull(servicePage.Find("[data-service-gp-jobs]")));

        Assert.Equal("/console/operate/geoprocessing", servicePage.Find("[data-service-gp-jobs]").GetAttribute("href"));
        Assert.Equal("/console/studio/processes", servicePage.Find("[data-service-gp-processes]").GetAttribute("href"));
    }

    private static async Task<ManifestBackedConsoleCapabilityManifest> CreateOptInDisabledManifestAsync()
    {
        var manifest = new ManifestBackedConsoleCapabilityManifest(new StaticRegistry(new CapabilityRegistrySnapshot
        {
            Bound = true,
            Descriptors =
            [
                new CapabilityDescriptor { Id = "alerts.geofence", Available = false, Supported = true, ReasonCode = "opt-in-disabled", OptInRequired = true, Lifecycle = "Preview" },
                new CapabilityDescriptor { Id = "gitops.release-manifest", Available = false, Supported = true, ReasonCode = "opt-in-disabled", OptInRequired = true, Lifecycle = "Preview" },
            ]
        }));
        await manifest.RefreshAsync();
        return manifest;
    }

    private static BunitContext CreateServiceContext(OperateServiceDetail detail)
    {
        var ctx = new BunitContext();
        ctx.Services.AddSingleton<NavigationManager>(new MountedNavigationManager("/console/"));
        ctx.Services.AddSingleton(new ConsoleV4Options { Enabled = true });
        ctx.Services.AddSingleton<ConsoleAgentContextRegistry>();
        ctx.AddConsoleNotifications();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IOperateTransitionDataSource>(new FixedServicesDataSource(detail));
        ctx.Services.AddSingleton<IServiceConfigurationOperation>(new UnsupportedServiceConfigurationOperation());
        return ctx;
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

    private sealed class MutableFolderClient : UnsupportedConsoleFolderClient
    {
        private readonly List<Folder> _folders;
        public int Creates { get; private set; }
        public TaskCompletionSource<OperateSectionResult<ConsoleFolderCatalog>>? PendingRead { get; init; }

        public MutableFolderClient(IEnumerable<string>? initialFolderNames = null)
        {
            _folders = (initialFolderNames ?? [])
                .Select(name => new Folder { Metadata = new() { Id = name, Name = name } })
                .ToList();
        }

        public override Task<OperateSectionResult<ConsoleFolderCatalog>> ReadAsync(CancellationToken cancellationToken = default) =>
            PendingRead?.Task.WaitAsync(cancellationToken)
            ?? Task.FromResult(OperateSectionResult<ConsoleFolderCatalog>.Allowed(new([.. _folders], "\"etag-1\"", "authority")));

        public override Task<ConsoleFolderMutationResult> CreateAsync(ConsoleFolderCatalog catalog, CreateFolderRequest request, CancellationToken cancellationToken = default)
        {
            Creates++;
            _folders.Add(new Folder { Metadata = new() { Id = request.Name, Name = request.Name, Title = request.Title } });
            return Task.FromResult(new ConsoleFolderMutationResult { Succeeded = true, State = "Saved" });
        }
    }

    private sealed class StaticRegistry(CapabilityRegistrySnapshot snapshot) : ICapabilityRegistryClient
    {
        public Task<CapabilityRegistrySnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(snapshot);
    }

    private sealed class StubAlertInboxClient : IConsoleAlertInboxClient
    {
        public Task<OperateSectionResult<ObservabilityAlertEventResponse>> AcknowledgeAlertAsync(
            ConsoleAlertAcknowledgement command, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<OperateSectionResult<ObservabilityAlertEventResponse>> ChangeAlertLifecycleAsync(
            ConsoleAlertLifecycleCommand command, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<OperateSectionResult<ConsoleAlertInboxSnapshot>> ListAlertsAsync(
            CancellationToken cancellationToken = default, string? cursor = null, ConsoleAlertAuthority? expectedAuthority = null) =>
            throw new InvalidOperationException("Must not query alert inbox when alerting is not advertised.");
    }
}
