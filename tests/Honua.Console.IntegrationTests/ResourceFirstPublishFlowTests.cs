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

    // ----------------------------------------------------------------- helpers

    private static OperateResourceEditPreview EditPreview(string id, string name) =>
        new(id, name, "file", "—", "Valid",
            new OperateBlastRadius([], [], [], [], [], []), [], []);

    private static OperateServiceLayerProjection Layer(int layerId, string canonicalResourceId) =>
        new(layerId, $"layer{layerId}", "Polygon", canonicalResourceId, canonicalResourceId);

    private static OperateServiceDetail Service(string name, string serviceType, params OperateServiceLayerProjection[] layers) =>
        new(name, name, serviceType, "Running", "managed", layers, [], []);

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
