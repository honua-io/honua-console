using Bunit;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.Components.Operate;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free coverage for the resource-first publish flow (redesign Phase 1) and the resource→publications
/// treeview (Phase 2). Asserts the protocol→publication mapping authority, the tree builder's grouping of
/// services under their canonical resource, the flow host's step rail + existing-resource skip + the
/// first-class missing-binding surfacing (never a fabricated step), the protocol-toggle picker, and the
/// redirect of the old entry points into the unified flow.
/// </summary>
public sealed class ResourceFirstPublishFlowTests
{
    private static Bunit.TestContext NewContext()
    {
        var ctx = new Bunit.TestContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        return ctx;
    }

    private static void RegisterMissingBinding(Bunit.TestContext ctx)
    {
        ctx.Services.AddSingleton<IConsoleFileImportOperation>(new UnsupportedConsoleFileImportOperation());
        ctx.Services.AddSingleton<IServiceLayerPublishOperation>(new UnsupportedServiceLayerPublishOperation());
        ctx.Services.AddSingleton<IOperateTransitionDataSource>(new UnsupportedOperateTransitionDataSource());
        ctx.Services.AddSingleton<IStudioMapStyleCatalogDataSource>(new UnsupportedStudioMapStyleCatalogDataSource());
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
        Assert.Equal("/city/parcels/FeatureServer/0", plans[0].Route);
        Assert.Equal("Stac", plans[1].ProtocolId);
        Assert.Equal("/stac/collections/parcels", plans[1].Route);
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
        Assert.Equal("/city/parcels/FeatureServer/0", parcels.Publications[0].Route);
        Assert.Equal("/city/parcels/MapServer", parcels.Publications[1].Route);
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
        Assert.Equal("/city/parcels/FeatureServer/0", parcels.Publications[0].Route);

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

        var cut = ctx.RenderComponent<ResourcePublicationsTree>(p => p.Add(c => c.Nodes, nodes));

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

        var cut = ctx.RenderComponent<PublishProtocolPicker>(p => p
            .Add(c => c.Enabled, PublishProtocolCatalog.DefaultEnabled)
            .Add(c => c.ServiceSlot, "city/parcels"));

        // Three default protocols checked → preview says 3 publications.
        Assert.Contains("data-publication-preview", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Will create 3 publications", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("data-protocol=\"FeatureServer\"", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Picker_TogglingAProtocol_RaisesTheNewEnabledSetInCatalogOrder()
    {
        using var ctx = NewContext();
        IReadOnlyCollection<string>? raised = null;

        var cut = ctx.RenderComponent<PublishProtocolPicker>(p => p
            .Add(c => c.Enabled, new[] { "FeatureServer" })
            .Add(c => c.EnabledChanged, set => raised = set));

        // Enable WMS — the raised set must be ordered by the catalog (FeatureServer before Wms).
        cut.Find("input[data-protocol=\"Wms\"]").Change(true);

        Assert.NotNull(raised);
        Assert.Equal(new[] { "FeatureServer", "Wms" }, raised!.ToArray());
    }

    // ----------------------------------------------------------------- flow host

    [Fact]
    public void Flow_RendersStepRailAndDriverToggle_ManualByDefault()
    {
        using var ctx = NewContext();
        RegisterMissingBinding(ctx);

        var cut = ctx.RenderComponent<DataToPublishFlow>();

        Assert.Contains("data-step-rail", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("data-step=\"AddData\"", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("data-step=\"Publish\"", cut.Markup, StringComparison.Ordinal);
        // Manual driver is the default surface; AddData step body is shown.
        Assert.Contains("data-step-body=\"AddData\"", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Flow_AiDriver_ShowsTheDeferredOutcomeApprovalSeam_NotAFabricatedFlow()
    {
        using var ctx = NewContext();
        RegisterMissingBinding(ctx);

        var cut = ctx.RenderComponent<DataToPublishFlow>(p => p.Add(c => c.InitialDriver, "ai"));

        // The AI driver is a structural seam (Phase 3): it surfaces the outcome+approval intent, not a built flow.
        Assert.Contains("data-ai-driver-seam", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Phase 3", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Flow_ExistingResourceSource_SkipsIngestAndLandsOnPublish()
    {
        using var ctx = NewContext();
        // The existing-resource path needs the resource list; an Unsupported source yields an empty list,
        // so seed a tiny in-test data source that advertises one resource.
        ctx.Services.AddSingleton<IConsoleFileImportOperation>(new UnsupportedConsoleFileImportOperation());
        ctx.Services.AddSingleton<IServiceLayerPublishOperation>(new UnsupportedServiceLayerPublishOperation());
        ctx.Services.AddSingleton<IStudioMapStyleCatalogDataSource>(new UnsupportedStudioMapStyleCatalogDataSource());
        ctx.Services.AddSingleton<IOperateTransitionDataSource>(new OneResourceDataSource("rsc_parcels", "parcels"));

        var cut = ctx.RenderComponent<DataToPublishFlow>(p => p.Add(c => c.InitialSource, "existingresource"));

        // Choose the existing resource, then continue — the flow must skip ② Resource and land on ③ Publish.
        cut.Find("select[aria-label=\"Existing resource\"]").Change("rsc_parcels");
        cut.Find("button.console-button").Click();

        Assert.Contains("data-step-body=\"Publish\"", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("data-publish-protocol-picker", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Flow_FileIngest_WithNoServer_SurfacesMissingBindingAsFirstClassState()
    {
        using var ctx = NewContext();
        RegisterMissingBinding(ctx);

        var cut = ctx.RenderComponent<DataToPublishFlow>(p => p.Add(c => c.InitialSource, "file"));

        // Upload a file, then continue: the Unsupported import returns a missing-binding result, which the
        // host renders as a first-class "unsupported" panel — never a fabricated successful ingest.
        var file = InputFileContent.CreateFromText("{}", "parcels.geojson");
        cut.FindComponent<Microsoft.AspNetCore.Components.Forms.InputFile>().UploadFiles(file);
        cut.Find("button.console-button").Click();

        Assert.Contains("data-flow-missing-binding", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("data-step-body=\"Resource\"", cut.Markup, StringComparison.Ordinal);
    }

    // ----------------------------------------------------------------- redirects

    [Theory]
    [InlineData("operate/resources", "/operate/data")]
    [InlineData("operate/layers", "/operate/data")]
    [InlineData("operate/services", "/operate/data?view=services")]
    [InlineData("operate/resources/import", "/operate/data/new?source=file")]
    [InlineData("operate/publishing/quick", "/operate/data/new?source=table")]
    [InlineData("operate/import/service", "/operate/data/new?source=remoteservice")]
    public void Redirects_FoldOldEntryPointsIntoTheUnifiedFlow(string from, string expectedTarget)
    {
        using var ctx = NewContext();
        var nav = ctx.Services.GetRequiredService<Bunit.TestDoubles.FakeNavigationManager>();
        nav.NavigateTo(from);

        ctx.RenderComponent<OperateDataRedirects>();

        Assert.EndsWith(expectedTarget, nav.Uri, StringComparison.Ordinal);
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

        var cut = ctx.RenderComponent<DataToPublishFlow>(p => p.Add(c => c.InitialSource, "remoteservice"));

        Assert.Contains("data-remote-service-import", cut.Markup, StringComparison.Ordinal);
        // The embedded import surface (OperateImportServicePage) renders its discovery form.
        Assert.Contains("Import from a service", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("use the dedicated import surface", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("dedicated import surface", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Flow_TablePublish_ThreadsPlannedProtocols_AndReportsOnlyThoseTheServerExposes()
    {
        using var ctx = NewContext();
        var publish = new RecordingPublishOperation(
            // The server exposes only FeatureServer + Stac (MapServer was not enabled), so the flow must
            // report exactly those as live — never over-report the un-exposed MapServer publication.
            enabledProtocols: ["FeatureServer", "Stac"]);
        ctx.Services.AddSingleton<IConsoleFileImportOperation>(new UnsupportedConsoleFileImportOperation());
        ctx.Services.AddSingleton<IServiceLayerPublishOperation>(publish);
        ctx.Services.AddSingleton<IOperateTransitionDataSource>(new OneConnectionDataSource("conn1", "Primary"));
        ctx.Services.AddSingleton<IStudioMapStyleCatalogDataSource>(new UnsupportedStudioMapStyleCatalogDataSource());

        var cut = ctx.RenderComponent<DataToPublishFlow>(p => p.Add(c => c.InitialSource, "table"));

        // Choose the connection so the table picker loads its single table.
        var chosen = new AddDataIntake.DataSourceIntake(AddDataMode.Table, "conn1");
        await cut.InvokeAsync(() => InvokePrivate(cut.Instance, "OnSourceChosen", chosen));
        cut.Render();

        // Select the table, then advance one primary action per step to Go live.
        cut.Find("[data-table-picker]").Change("public.parcels");
        cut.Find("[data-step-body=\"AddData\"] button.console-button").Click(); // → Resource
        cut.WaitForElement("[data-step-body=\"Resource\"]");
        cut.Find("[data-step-body=\"Resource\"] button.console-button").Click(); // → Publish
        cut.WaitForElement("[data-step-body=\"Publish\"]");
        cut.Find("[data-step-body=\"Publish\"] button.console-button").Click(); // → Style
        cut.WaitForElement("[data-step-body=\"Style\"]");
        cut.Find("[data-step-body=\"Style\"] button.console-button").Click(); // → Go live
        cut.WaitForElement("[data-step-body=\"GoLive\"]");
        cut.Find("[data-step-body=\"GoLive\"] button.console-button").Click(); // Apply & publish

        // The flow lands on Done once at least one protocol is genuinely live.
        cut.WaitForElement("[data-step-body=\"Done\"]");

        // The layer publish ran exactly once (not once per protocol), and carried the planned protocol set.
        Assert.Equal(1, publish.PublishCalls);
        Assert.Equal(1, publish.EnableCalls);
        Assert.Equal(new[] { "FeatureServer", "MapServer", "Stac" }, publish.LastCommand!.Protocols.ToArray());
        // Exactly one protocol-enablement call carrying the same plan.
        Assert.Equal(new[] { "FeatureServer", "MapServer", "Stac" }, publish.LastEnableProtocols!.ToArray());

        // Only the 2 protocols the server actually exposes (FeatureServer + Stac) are reported live —
        // the un-exposed MapServer is NOT over-reported (2 of 3 publications landed).
        Assert.Contains("2 of 3 publications landed", cut.Markup, StringComparison.Ordinal);
    }

    private static object? InvokePrivate(object target, string method, params object[] args) =>
        target.GetType()
            .GetMethod(method, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(target, args);

    // ----------------------------------------------------------------- helpers

    private static OperateResourceEditPreview EditPreview(string id, string name) =>
        new(id, name, "file", "—", "Valid",
            new OperateBlastRadius([], [], [], [], [], []), [], []);

    private static OperateServiceLayerProjection Layer(int layerId, string canonicalResourceId) =>
        new(layerId, $"layer{layerId}", "Polygon", canonicalResourceId, canonicalResourceId);

    private static OperateServiceDetail Service(string name, string serviceType, params OperateServiceLayerProjection[] layers) =>
        new(name, name, serviceType, "Running", "managed", layers, [], []);

    /// <summary>A data source advertising exactly one connection, for the table-publish flow test.</summary>
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

    /// <summary>
    /// A recording publish operation: returns one table, records the publish command (so a test can assert the
    /// planned protocols were threaded), and reports a controllable canonical protocol set from EnableProtocols.
    /// </summary>
    private sealed class RecordingPublishOperation(IReadOnlyList<string> enabledProtocols) : IServiceLayerPublishOperation
    {
        public int PublishCalls { get; private set; }
        public int EnableCalls { get; private set; }
        public ServiceLayerPublishCommand? LastCommand { get; private set; }
        public IReadOnlyList<string>? LastEnableProtocols { get; private set; }

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

        public Task<ServiceProtocolEnableResult> EnableProtocolsAsync(
            string serviceName, IReadOnlyList<string> protocols, CancellationToken cancellationToken = default)
        {
            EnableCalls++;
            LastEnableProtocols = protocols;
            return Task.FromResult(new ServiceProtocolEnableResult
            {
                Succeeded = true,
                State = "Published",
                EnabledProtocols = enabledProtocols,
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
                    GeometryType = "Polygon",
                    Srid = 4326,
                    EstimatedRows = 10,
                    Columns = ["id", "name"],
                },
            ]);
    }

    /// <summary>A minimal data source that advertises exactly one resource, for the existing-resource path test.</summary>
    private sealed class OneResourceDataSource(string resourceId, string name) : IOperateTransitionDataSource
    {
        private readonly OperateResourceEditPreview _resource =
            EditPreview(resourceId, name);

        public Task<OperateTransitionWorkspace> GetWorkspaceAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new OperateTransitionWorkspace([], [_resource], [], [], []));

        public Task<OperateConnectionSummary?> FindConnectionAsync(string connectionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<OperateConnectionSummary?>(null);

        public Task<OperateResourceEditPreview?> FindResourceEditAsync(string resourceId, CancellationToken cancellationToken = default) =>
            Task.FromResult<OperateResourceEditPreview?>(_resource);

        public Task<OperateServiceDetail?> FindServiceAsync(string serviceName, CancellationToken cancellationToken = default) =>
            Task.FromResult<OperateServiceDetail?>(null);
    }
}
