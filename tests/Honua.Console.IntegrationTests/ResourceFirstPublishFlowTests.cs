using System.Reflection;
using Bunit;
using Honua.Console.Shell;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.Components.Operate;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class ResourceFirstPublishFlowTests
{
    [Fact]
    public void QuickPublish_GenericGeometryRequiresExplicitTypeAndPassesTheChoice()
    {
        using var ctx = NewContext();
        var publish = new RecordingPublishOperation("GEOMETRY");
        ctx.Services.AddSingleton<IServiceLayerPublishOperation>(publish);
        ctx.Services.AddSingleton<IOperateTransitionDataSource>(new OneConnectionDataSource("conn", "Source"));
        var page = ctx.Render<OperatePublishLayerPage>();
        page.Find("select[aria-label='Connection']").Change("conn");
        page.Find("select[aria-label='Table']").Change("public.parcels");
        page.Find("input[placeholder='parcels-fs']").Input("parcels-fs");
        page.Find("input[placeholder='Parcels']").Input("Parcels");
        page.Find("[data-workspace-publish]").Click();
        Assert.Equal(0, publish.PublishCalls);
        Assert.Contains("Select the geometry type", page.Markup, StringComparison.Ordinal);
        page.Find("select[aria-label='Geometry type']").Change("Polygon");
        page.Find("[data-workspace-publish]").Click();
        Assert.Equal(1, publish.PublishCalls);
        Assert.Equal("Polygon", publish.LastCommand?.GeometryType);
    }

    [Fact]
    public void QuickPublish_ConcreteGeometryNeedsNoOverride()
    {
        using var ctx = NewContext();
        var publish = new RecordingPublishOperation("Point");
        ctx.Services.AddSingleton<IServiceLayerPublishOperation>(publish);
        ctx.Services.AddSingleton<IOperateTransitionDataSource>(new OneConnectionDataSource("conn", "Source"));
        var page = ctx.Render<OperatePublishLayerPage>();
        page.Find("select[aria-label='Connection']").Change("conn");
        page.Find("select[aria-label='Table']").Change("public.parcels");
        Assert.Empty(page.FindAll("select[aria-label='Geometry type']"));
        page.Find("input[placeholder='parcels-fs']").Input("points-fs");
        page.Find("input[placeholder='Parcels']").Input("Points");
        page.Find("[data-workspace-publish]").Click();
        Assert.Equal("Point", publish.LastCommand?.GeometryType);
    }

    private static BunitContext NewContext()
    {
        var ctx = new BunitContext();
        ctx.AddConsoleNotifications();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        return ctx;
    }

    private static void RegisterMissingBinding(BunitContext ctx)
    {
        ctx.Services.AddSingleton<IConsoleFileImportOperation>(new UnsupportedConsoleFileImportOperation());
        ctx.Services.AddSingleton<IServiceLayerPublishOperation>(new UnsupportedServiceLayerPublishOperation());
        ctx.Services.AddSingleton<IOperateTransitionDataSource>(new UnsupportedOperateTransitionDataSource());
        ctx.Services.AddSingleton<IStudioMapStyleCatalogDataSource>(new UnsupportedStudioMapStyleCatalogDataSource());
    }

    [Fact]
    public void AddDataPage_WithLegacyAiQuery_RendersNoAiDriverEntryPoint()
    {
        using var ctx = NewContext();
        RegisterMissingBinding(ctx);
        ctx.Services.GetRequiredService<Bunit.TestDoubles.BunitNavigationManager>()
            .NavigateTo("/operate/data/new?source=ai&driver=ai");

        var cut = ctx.Render<OperateDataFlowPage>();

        Assert.Empty(cut.FindAll("[data-driver], [data-ai-driver], [data-intake-mode='Ai'], textarea"));
        Assert.Single(cut.FindAll("[data-workspace-open]"));
        Assert.Single(cut.FindAll("[data-step-body='AddData']"));
    }

    [Fact]
    public void ResourceReadFailure_ShowsOneRecoveryNoticeAndNeverAnEmptyTree()
    {
        using var ctx = NewContext();
        RegisterMissingBinding(ctx);
        var cut = ctx.Render<OperateDataPage>();
        cut.WaitForAssertion(() =>
        {
            Assert.Single(cut.FindAll("[data-connection-notice]"));
            Assert.Empty(cut.FindAll("[data-tree-empty], [data-resource-tree]"));
            Assert.Contains("Connect environment", cut.Markup, StringComparison.Ordinal);
        });
    }

    // ----------------------------------------------------------------- mapping authority

    [Fact]
    public void ProtocolCatalog_DefaultEnabled_IsFeatureServerMapServerStac()
    {
        Assert.Equal(
            new[] { "FeatureServer", "MapServer", "Stac" },
            PublishProtocolCatalog.DefaultEnabled.ToArray());
    }

    [Fact]
    public void PlanPublications_MapsEachEnabledProtocolToAPublicationWithRoute()
    {
        var plans = PublishProtocolCatalog.PlanPublications(
            "city/parcels",
            ["Stac", "FeatureServer"]); // deliberately out of catalog order

        // One publication per enabled protocol, in stable catalog order (FeatureServer before Stac).
        Assert.Equal(2, plans.Count);
        Assert.Equal("FeatureServer", plans[0].ProtocolId);
        Assert.Equal("esri-feature-service", plans[0].ServiceType);
        Assert.Equal("/rest/services/city%2Fparcels/FeatureServer", plans[0].Route);
        Assert.Equal("Stac", plans[1].ProtocolId);
        Assert.Equal("/stac", plans[1].Route);
    }

    [Fact]
    public void PlanPublications_SkipsUnknownProtocols_NeverFabricates()
    {
        var plans = PublishProtocolCatalog.PlanPublications("svc", ["FeatureServer", "NotAProtocol"]);

        Assert.Single(plans);
        Assert.Equal("FeatureServer", plans[0].ProtocolId);
    }

    [Theory]
    [InlineData("FeatureServer", "esri-feature-service")]
    [InlineData("MapServer", "esri-map-service")]
    [InlineData("Stac", "stac-api")]
    [InlineData("Wfs20", "wfs")]
    [InlineData("OgcFeatures", "ogc-api-features")]
    public void Find_ResolvesProtocolToItsMetadataV2ServiceType(string protocolId, string serviceType)
    {
        var descriptor = PublishProtocolCatalog.Find(protocolId);
        Assert.NotNull(descriptor);
        Assert.Equal(serviceType, descriptor!.ServiceType);
    }

    [Theory]
    // metadata-v2 service-type strings.
    [InlineData("esri-feature-service", new[] { "FeatureServer" })]
    [InlineData("stac-api", new[] { "Stac" })]
    // single ServiceProtocols ids.
    [InlineData("FeatureServer", new[] { "FeatureServer" })]
    // honua-server joins enabled protocols → catalog-ordered protocol set, never fabricated.
    [InlineData("FeatureServer, MapServer", new[] { "FeatureServer", "MapServer" })]
    [InlineData("MapServer, FeatureServer", new[] { "FeatureServer", "MapServer" })]
    // display names the in-memory/demo source uses.
    [InlineData("Feature service", new[] { "FeatureServer" })]
    // the "Geo service" fallback maps to nothing (no protocol claimed).
    [InlineData("Geo service", new string[0])]
    public void ResolveServiceTypeProtocols_MapsDisplayAndProtocolValuesToProtocolIds(string serviceType, string[] expected)
    {
        var ids = PublishProtocolCatalog.ResolveServiceTypeProtocols(serviceType)
            .Select(d => d.Id)
            .ToArray();

        Assert.Equal(expected, ids);
    }

    [Fact]
    public void TreeBuilder_WithJoinedProtocolServiceType_ShowsResourceRunningWithPublications()
    {
        // Regression: existing server data populates ServiceType as joined protocol names ("FeatureServer,
        // MapServer"), not the metadata-v2 strings — the tree must still mark the resource Running with its
        // protocol publications rather than falling through to Draft.
        IReadOnlyList<OperateResourceEditPreview> resources = [EditPreview("rsc_parcels", "parcels")];
        IReadOnlyList<OperateServiceDetail> services =
        [
            Service("city/parcels", "FeatureServer, MapServer", Layer(1, "rsc_parcels")),
        ];

        var nodes = ResourcePublicationsTreeBuilder.Build(resources, services);

        var parcels = nodes.Single(node => node.ResourceId == "rsc_parcels");
        Assert.Equal("Running", parcels.Status);
        Assert.Equal(new[] { "FeatureServer", "MapServer" }, parcels.Publications.Select(p => p.ProtocolId).ToArray());
        Assert.Equal("/rest/services/city%2Fparcels/FeatureServer", parcels.Publications[0].Route);
        Assert.Equal("/rest/services/city%2Fparcels/MapServer", parcels.Publications[1].Route);
    }

    // ----------------------------------------------------------------- tree builder

    [Fact]
    public void TreeBuilder_GroupsServicePublicationsUnderTheirCanonicalResource()
    {
        IReadOnlyList<OperateResourceEditPreview> resources =
        [
            EditPreview("rsc_parcels", "parcels"),
            EditPreview("rsc_zoning", "zoning"),
        ];
        IReadOnlyList<OperateServiceDetail> services =
        [
            Service("city/parcels", "esri-feature-service", Layer(1, "rsc_parcels")),
            Service("stac/parcels", "stac-api", Layer(2, "rsc_parcels")),
        ];

        var nodes = ResourcePublicationsTreeBuilder.Build(resources, services);

        var parcels = nodes.Single(node => node.ResourceId == "rsc_parcels");
        Assert.Equal("Running", parcels.Status);
        Assert.Equal(new[] { "FeatureServer", "Stac" }, parcels.Publications.Select(p => p.ProtocolId).ToArray());
        Assert.Equal("/rest/services/city%2Fparcels/FeatureServer", parcels.Publications[0].Route);

        // A resource with no published service layer is a draft node with no publications (not fabricated).
        var zoning = nodes.Single(node => node.ResourceId == "rsc_zoning");
        Assert.Equal("Draft", zoning.Status);
        Assert.Empty(zoning.Publications);
    }

    [Fact]
    public void Tree_RendersResourceNodesAndDraftPublishAffordance()
    {
        using var ctx = NewContext();
        IReadOnlyList<ResourceTreeNode> nodes =
        [
            new("rsc_parcels", "parcels", "file", "Running", 0, null,
                [new("FeatureServer", "FeatureServer", true, "/city/parcels/FeatureServer/0")]),
            new("rsc_zoning", "zoning", "table", "Draft", 0, null, []),
        ];

        var cut = ctx.Render<ResourcePublicationsTree>(p => p.Add(c => c.Nodes, nodes));

        Assert.Contains("data-resource-node=\"rsc_parcels\"", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("data-resource-node=\"rsc_zoning\"", cut.Markup, StringComparison.Ordinal);
        // The draft resource exposes a Publish → affordance into the flow.
        Assert.Contains("source=existingresource", cut.Markup, StringComparison.Ordinal);
    }

    // ----------------------------------------------------------------- protocol picker

    [Fact]
    public void Picker_RendersDefaultEnabledAndPublicationPreview()
    {
        using var ctx = NewContext();

        var cut = ctx.Render<PublishProtocolPicker>(p => p
            .Add(c => c.Enabled, PublishProtocolCatalog.DefaultEnabled)
            .Add(c => c.ServiceSlot, "city/parcels"));

        Assert.Contains("data-publication-preview", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Will serve through: FeatureServer · MapServer · STAC API", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("data-protocol=\"FeatureServer\"", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Picker_TogglingAProtocol_RaisesTheNewEnabledSetInCatalogOrder()
    {
        using var ctx = NewContext();
        IReadOnlyCollection<string>? raised = null;

        var cut = ctx.Render<PublishProtocolPicker>(p => p
            .Add(c => c.Enabled, new[] { "FeatureServer" })
            .Add(c => c.EnabledChanged, set => raised = set));

        // Enable WMS — the raised set must be ordered by the catalog (FeatureServer before Wms).
        cut.Find("input[data-protocol=\"Wms\"]").Change(true);

        Assert.NotNull(raised);
        Assert.Equal(new[] { "FeatureServer", "Wms" }, raised!.ToArray());
    }

    // ----------------------------------------------------------------- flow host

    [Fact]
    public void Flow_RendersStepRailAndAddDataStep()
    {
        using var ctx = NewContext();
        RegisterMissingBinding(ctx);

        var cut = ctx.Render<DataToPublishFlow>();

        Assert.Contains("data-step-rail", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("data-step=\"AddData\"", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("data-step=\"Publish\"", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("data-step-body=\"AddData\"", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Flow_ExistingResourceSource_SkipsIngestAndLandsOnPublish()
    {
        using var ctx = NewContext();
        RegisterExistingResource(ctx);

        var cut = ctx.Render<DataToPublishFlow>(p => p.Add(c => c.InitialSource, "existingresource"));

        // Choose the existing resource, then continue — the flow must skip ② Resource and land on ③ Publish.
        cut.Find("select[aria-label=\"Existing resource\"]").Change("rsc_parcels");
        cut.Find("[data-step-body=\"AddData\"] button.console-button").Click();

        Assert.Contains("data-step-body=\"Publish\"", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("data-publish-protocol-picker", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Flow_ExistingResource_StartsFromItsCurrentServiceAndProtocols()
    {
        using var ctx = NewContext();
        RegisterExistingResource(ctx);

        var cut = ctx.Render<DataToPublishFlow>(p => p.Add(c => c.InitialSource, "existingresource"));
        cut.Find("select[aria-label=\"Existing resource\"]").Change("rsc_parcels");
        cut.Find("[data-step-body=\"AddData\"] button.console-button").Click();

        Assert.Equal("parcels", cut.Find("[data-service-slot]").TextContent);
        Assert.True(cut.Find("[data-protocol='FeatureServer']").HasAttribute("checked"));
        Assert.True(cut.Find("[data-protocol='MapServer']").HasAttribute("checked"));
        Assert.False(cut.Find("[data-protocol='Stac']").HasAttribute("checked"));
        Assert.Single(cut.FindAll("[data-service-fixed]"));
    }

    [Fact]
    public void Flow_ExistingResourceReachesGoLiveWithTheRealApply()
    {
        using var ctx = NewContext();
        RegisterExistingResource(ctx);
        ctx.Services.AddSingleton<IServiceConfigurationOperation>(new UnsupportedServiceConfigurationOperation());
        var cut = ctx.Render<DataToPublishFlow>(p => p.Add(c => c.InitialSource, "existingresource"));
        cut.Find("select[aria-label=\"Existing resource\"]").Change("rsc_parcels");
        cut.Find("[data-step-body=\"AddData\"] button.console-button").Click();
        cut.Find("[data-step-body=\"Publish\"] button.console-button").Click();
        cut.Find("[data-step-body=\"Style\"] button.console-button").Click();

        cut.Find("[data-go-live-apply]").Click();

        Assert.Equal(2, cut.FindAll("[data-protocol-result]").Count);
        Assert.DoesNotContain("publishes table sources directly", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Unsupported here", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Flow_FileSource_LinksToTheImportTaskInsteadOfAnInlineUpload()
    {
        using var ctx = NewContext();
        RegisterMissingBinding(ctx);

        var cut = ctx.Render<DataToPublishFlow>(p => p.Add(c => c.InitialSource, "file"));

        Assert.Single(cut.FindAll("a[href='/operate/resources/import/task']"));
        Assert.Empty(cut.FindAll("input[type='file']"));
        Assert.Empty(cut.FindAll("[data-step-body='AddData'] button.console-button"));
    }

    // ----------------------------------------------------------------- redirects

    [Theory]
    [InlineData("operate/resources", "/operate/data")]
    [InlineData("operate/resources/new", "/operate/data/new")]
    [InlineData("operate/alerts", "/operate/observability#alerts")]
    public void Redirects_FoldOldListAliasesIntoTheUnifiedFlow(string from, string expectedTarget)
    {
        using var ctx = NewContext();
        var nav = ctx.Services.GetRequiredService<Bunit.TestDoubles.BunitNavigationManager>();
        nav.NavigateTo(from);

        ctx.Render<OperateDataRedirects>();

        Assert.EndsWith(expectedTarget, nav.Uri, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/operate/resources/import", "OperateImportFilePage")]
    [InlineData("/operate/publishing/quick", "OperatePublishLayerPage")]
    [InlineData("/operate/import/service", "OperateImportServicePage")]
    [InlineData("/operate/layers", "OperateLayersPage")]
    [InlineData("/operate/services", "OperateServicesPage")]
    public void OldEntryPoints_WithADedicatedPage_AreOwnedByThatPage(string route, string expectedOwner)
    {
        var owners =
            (from type in typeof(ConsoleRoutes).Assembly.GetTypes()
             where typeof(IComponent).IsAssignableFrom(type)
             from attribute in type.GetCustomAttributes<RouteAttribute>(inherit: false)
             where string.Equals(attribute.Template, route, StringComparison.Ordinal)
             select type.Name)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal([expectedOwner], owners);
    }

    [Fact]
    public void Flow_RemoteServiceSource_EmbedsTheImportSurface_NotADeadEnd()
    {
        using var ctx = NewContext();
        // Remote-service import must be reachable through the unified flow (the old /operate/import/service
        // route redirects to source=remoteservice): the AddData step embeds the import surface rather than
        // dead-ending with "use the dedicated import surface".
        ctx.Services.AddSingleton<IConsoleFileImportOperation>(new UnsupportedConsoleFileImportOperation());
        ctx.Services.AddSingleton<IServiceLayerPublishOperation>(new UnsupportedServiceLayerPublishOperation());
        ctx.Services.AddSingleton<IOperateTransitionDataSource>(new UnsupportedOperateTransitionDataSource());
        ctx.Services.AddSingleton<IStudioMapStyleCatalogDataSource>(new UnsupportedStudioMapStyleCatalogDataSource());
        ctx.Services.AddSingleton<IConsoleServiceImportOperation>(new UnsupportedConsoleServiceImportOperation());

        var cut = ctx.Render<DataToPublishFlow>(p => p.Add(c => c.InitialSource, "remoteservice"));

        Assert.Contains("data-remote-service-import", cut.Markup, StringComparison.Ordinal);
        // The embedded import surface (OperateImportServicePage) renders its discovery form.
        Assert.Contains("Import from a service", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("use the dedicated import surface", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("dedicated import surface", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Flow_ChoosingATableConnection_HandsOffToQuickPublish()
    {
        using var ctx = NewContext();
        ctx.Services.AddSingleton<IConsoleFileImportOperation>(new UnsupportedConsoleFileImportOperation());
        ctx.Services.AddSingleton<IOperateTransitionDataSource>(new OneConnectionDataSource("conn1", "Primary"));
        ctx.Services.AddSingleton<IStudioMapStyleCatalogDataSource>(new UnsupportedStudioMapStyleCatalogDataSource());
        var nav = ctx.Services.GetRequiredService<Bunit.TestDoubles.BunitNavigationManager>();

        var cut = ctx.Render<DataToPublishFlow>(p => p.Add(c => c.InitialSource, "table"));
        cut.Find("select[aria-label=\"Connection\"]").Change("conn1");

        Assert.EndsWith("/operate/publishing/quick", nav.Uri, StringComparison.Ordinal);
    }

    // ----------------------------------------------------------------- helpers

    private static void RegisterExistingResource(BunitContext ctx)
    {
        ctx.Services.AddSingleton<IConsoleFileImportOperation>(new UnsupportedConsoleFileImportOperation());
        ctx.Services.AddSingleton<IStudioMapStyleCatalogDataSource>(new UnsupportedStudioMapStyleCatalogDataSource());
        ctx.Services.AddSingleton<IOperateTransitionDataSource>(new OneResourceDataSource("rsc_parcels", "parcels"));
    }

    private static OperateResourceEditPreview EditPreview(string id, string name) =>
        new(id, name, "file", "—", "Valid",
            new OperateBlastRadius([], [], [], [], [], []), [], []);

    private static OperateServiceLayerProjection Layer(int layerId, string canonicalResourceId) =>
        new(layerId, $"layer{layerId}", "Polygon", canonicalResourceId, canonicalResourceId);

    private static OperateServiceDetail Service(string name, string serviceType, params OperateServiceLayerProjection[] layers) =>
        new(name, name, serviceType, "Running", "managed", layers, [], []);

    private sealed class OneConnectionDataSource(string connectionId, string name) : IOperateTransitionDataSource
    {
        private readonly OperateConnectionSummary _connection =
            new(connectionId, name, "PostGIS", "db", "principal", "Connected", "now", null);

        public Task<OperateTransitionWorkspace> GetWorkspaceAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new OperateTransitionWorkspace([_connection], [], [], [], []));

        public Task<OperateConnectionSummary?> FindConnectionAsync(string connectionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<OperateConnectionSummary?>(_connection);

        public Task<OperateResourceEditPreview?> FindResourceEditAsync(string resourceId, CancellationToken cancellationToken = default) =>
            Task.FromResult<OperateResourceEditPreview?>(null);

        public Task<OperateServiceDetail?> FindServiceAsync(string serviceName, CancellationToken cancellationToken = default) =>
            Task.FromResult<OperateServiceDetail?>(null);
    }

    private sealed class RecordingPublishOperation(string geometryType) : IServiceLayerPublishOperation
    {
        public int PublishCalls { get; private set; }
        public ServiceLayerPublishCommand? LastCommand { get; private set; }

        public Task<ServiceLayerPublishResult> PublishAsync(
            ServiceLayerPublishCommand command, CancellationToken cancellationToken = default)
        {
            PublishCalls++;
            LastCommand = command;
            return Task.FromResult(new ServiceLayerPublishResult
            {
                Succeeded = true,
                State = "Published",
                LayerId = 1,
                LayerName = command.LayerName,
                ServiceName = command.ServiceName,
            });
        }

        public Task<IReadOnlyList<ServiceLayerPublishTable>> ListTablesAsync(
            string connectionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ServiceLayerPublishTable>>(
            [
                new ServiceLayerPublishTable
                {
                    Schema = "public",
                    Table = "parcels",
                    GeometryColumn = "geom",
                    GeometryType = geometryType,
                    Srid = 4326,
                    EstimatedRows = 10,
                    Columns = ["id", "name"],
                },
            ]);
    }

    private sealed class OneResourceDataSource(string resourceId, string name) : IOperateTransitionDataSource
    {
        private readonly OperateServiceDetail _service =
            Service("parcels", "FeatureServer, MapServer", new OperateServiceLayerProjection(1, name, "Polygon", resourceId, name));

        public Task<OperateTransitionWorkspace> GetWorkspaceAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new OperateTransitionWorkspace([], [EditPreview(resourceId, name)], [_service], [], []));

        public Task<OperateConnectionSummary?> FindConnectionAsync(string connectionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<OperateConnectionSummary?>(null);

        public Task<OperateResourceEditPreview?> FindResourceEditAsync(string resourceId, CancellationToken cancellationToken = default) =>
            Task.FromResult<OperateResourceEditPreview?>(null);

        public Task<OperateServiceDetail?> FindServiceAsync(string serviceName, CancellationToken cancellationToken = default) =>
            Task.FromResult<OperateServiceDetail?>(_service);
    }
}
