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

/// <summary>
/// Docker-free coverage for the resource-first publish flow (redesign Phase 1) and the resource→publications
/// treeview (Phase 2). Asserts the protocol→publication mapping authority, the tree builder's grouping of
/// services under their canonical resource, the flow host's step rail + existing-resource skip + the
/// first-class missing-binding surfacing (never a fabricated step), the protocol-toggle picker, and the
/// redirect of the old entry points into the unified flow.
/// </summary>
public sealed class ResourceFirstPublishFlowTests
{
    private static BunitContext NewContext()
    {
        var ctx = new BunitContext();
        ctx.AddConsoleNotifications();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        // DataToPublishFlow injects IAiPublishDriver; provide the missing-binding default so
        // every render resolves. Tests that exercise AI mode register their own driver after
        // (last registration wins), so this only covers the non-AI render paths.
        ctx.Services.AddSingleton<IAiPublishDriver>(new UnsupportedAiPublishDriver());
        return ctx;
    }

    private static void RegisterMissingBinding(BunitContext ctx)
    {
        ctx.Services.AddSingleton<IConsoleFileImportOperation>(new UnsupportedConsoleFileImportOperation());
        ctx.Services.AddSingleton<IServiceLayerPublishOperation>(new UnsupportedServiceLayerPublishOperation());
        ctx.Services.AddSingleton<IOperateTransitionDataSource>(new UnsupportedOperateTransitionDataSource());
        ctx.Services.AddSingleton<IStudioMapStyleCatalogDataSource>(new UnsupportedStudioMapStyleCatalogDataSource());
        ctx.Services.AddSingleton<IAiPublishDriver>(new UnsupportedAiPublishDriver());
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

        var cut = ctx.Render<ResourcePublicationsTree>(p => p.Add(c => c.Nodes, nodes));

        Assert.Contains("data-resource-node=\"rsc_parcels\"", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("data-resource-node=\"rsc_zoning\"", cut.Markup, StringComparison.Ordinal);
        // The draft resource exposes a Publish → affordance into the flow.
        Assert.Contains("source=existingresource", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Tree_UsesNativeListAndButtonSemantics_NotAnIncompleteAriaTree()
    {
        using var ctx = NewContext();
        IReadOnlyList<ResourceTreeNode> nodes =
        [
            new("rsc_parcels", "parcels", "file", "Running", 0, null,
                [new("FeatureServer", "FeatureServer", true, "/city/parcels/FeatureServer/0")]),
        ];

        var cut = ctx.Render<ResourcePublicationsTree>(p => p.Add(c => c.Nodes, nodes));

        Assert.Empty(cut.FindAll("[role='tree'], [role='treeitem'], [role='group']"));
        Assert.Equal("button", cut.Find(".resource-tree__name").TagName, ignoreCase: true);
        Assert.Equal("false", cut.Find(".resource-tree__toggle").GetAttribute("aria-expanded"));
    }

    [Fact]
    public void Tree_FilterWithNoMatches_DoesNotClaimTheEnvironmentHasNoResources()
    {
        using var ctx = NewContext();
        IReadOnlyList<ResourceTreeNode> nodes =
        [
            new("rsc_parcels", "parcels", "file", "Draft", 0, null, []),
        ];

        var cut = ctx.Render<ResourcePublicationsTree>(p => p.Add(c => c.Nodes, nodes));
        cut.Find("input[type='search']").Input("wetlands");

        Assert.Contains("No resources match your filter", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("No resources yet", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void AddDataIntake_UsesPressedButtonsInsteadOfIncompleteTabSemantics()
    {
        using var ctx = NewContext();

        var cut = ctx.Render<AddDataIntake>();

        Assert.Empty(cut.FindAll("[role='tablist'], [role='tab']"));
        Assert.Equal("true", cut.Find("[data-intake-mode='File']").GetAttribute("aria-pressed"));
        Assert.Equal("false", cut.Find("[data-intake-mode='Table']").GetAttribute("aria-pressed"));
    }

    [Fact]
    public void OperateData_MissingBinding_DoesNotRenderAnEmptyResourceClaim()
    {
        using var ctx = NewContext();
        ctx.Services.AddSingleton<IOperateTransitionDataSource>(new UnsupportedOperateTransitionDataSource());

        var cut = ctx.Render<OperateDataPage>();

        Assert.Contains("Connect an environment to browse data and layers", cut.Markup, StringComparison.Ordinal);
        Assert.NotEmpty(cut.FindAll("a[href='/environments/new']"));
        Assert.DoesNotContain("No resources yet", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void OperateData_AuthoritativeEmptyRead_RendersTheAddDataEmptyState()
    {
        using var ctx = NewContext();
        ctx.Services.AddSingleton<IOperateTransitionDataSource>(new WorkspaceDataSource(
            new OperateTransitionWorkspace([], [], [], [], [])));

        var cut = ctx.Render<OperateDataPage>();

        Assert.Contains("No resources yet", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("/operate/data/new", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void OperateData_UnavailableRead_RendersCapabilityStateNotEmptyState()
    {
        using var ctx = NewContext();
        ctx.Services.AddSingleton<IOperateTransitionDataSource>(new WorkspaceDataSource(
            new OperateTransitionWorkspace(
                [], [], [], [],
                [new OperateCapabilityState("Services", "Unavailable", "GET /api/v1/admin/services", "The connected server could not be reached.")])));

        var cut = ctx.Render<OperateDataPage>();

        Assert.Contains("The connected server could not be reached.", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("No resources yet", cut.Markup, StringComparison.Ordinal);
    }

    // ----------------------------------------------------------------- protocol picker

    [Fact]
    public void Picker_RendersDefaultEnabledAndPublicationPreview()
    {
        using var ctx = NewContext();

        var cut = ctx.Render<PublishProtocolPicker>(p => p
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
    public void Flow_RendersStepRailAndDriverToggle_AiByDefault()
    {
        using var ctx = NewContext();
        RegisterAiServer(ctx, FakeAiPublishDriver.Enabled());

        var cut = ctx.Render<DataToPublishFlow>();

        Assert.Contains("data-step-rail", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("data-step=\"AddData\"", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("data-step=\"Publish\"", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("data-ai-driver", cut.Markup, StringComparison.Ordinal);
        Assert.Equal("true", cut.Find("[data-driver='ai']").GetAttribute("aria-pressed"));
        Assert.Equal("false", cut.Find("[data-driver='manual']").GetAttribute("aria-pressed"));
    }

    [Fact]
    public void Flow_WithNoServer_RequiresEnvironmentBeforeRenderingUploadControls()
    {
        using var ctx = NewContext();
        RegisterMissingBinding(ctx);

        var cut = ctx.Render<DataToPublishFlow>();

        Assert.Contains("data-flow-missing-binding", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Connect an environment before adding data", cut.Markup, StringComparison.Ordinal);
        Assert.NotEmpty(cut.FindAll("a[href='/environments/new']"));
        Assert.Empty(cut.FindAll("input[type='file']"));
        Assert.DoesNotContain("data-step-rail", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Flow_UsesCapabilityStateForMissingBinding_NotConcreteSourceType()
    {
        using var ctx = NewContext();
        RegisterMissingBinding(ctx);
        ctx.Services.AddSingleton<IOperateTransitionDataSource>(new WorkspaceDataSource(
            new OperateTransitionWorkspace(
                [], [], [], [],
                [new OperateCapabilityState("Operate", "Missing binding", "server", "No environment is connected.")])));

        var cut = ctx.Render<DataToPublishFlow>();

        Assert.Contains("Connect an environment before adding data", cut.Markup, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("input[type='file']"));
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
        ctx.Services.AddSingleton<IAiPublishDriver>(new UnsupportedAiPublishDriver());
        ctx.Services.AddSingleton<IOperateTransitionDataSource>(new OneResourceDataSource("rsc_parcels", "parcels"));

        var cut = ctx.Render<DataToPublishFlow>(p => p.Add(c => c.InitialSource, "existingresource"));

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

        var cut = ctx.Render<DataToPublishFlow>(p => p.Add(c => c.InitialSource, "file"));

        // The binding requirement is surfaced before a user picks a file, not after a dead-end upload.
        Assert.Contains("data-flow-missing-binding", cut.Markup, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("input[type='file']"));
        Assert.DoesNotContain("data-step-body=\"Resource\"", cut.Markup, StringComparison.Ordinal);
    }

    // ----------------------------------------------------------------- AI driver mapping (pure)

    [Fact]
    public void AiProposal_InfersProtocolsFromIntentWords_InCatalogOrder()
    {
        var protocols = AiPublishProposalSummary.InferProtocols(
            "publish Maui parcels as a STAC catalog and a feature service");

        // Recognised words → FeatureServer + Stac, ordered by the catalog (FeatureServer before Stac).
        Assert.Equal(new[] { "FeatureServer", "Stac" }, protocols.ToArray());
    }

    [Fact]
    public void AiProposal_NoProtocolWords_FallsBackToDefaultEnabledSet()
    {
        var protocols = AiPublishProposalSummary.InferProtocols("just publish the parcels please");

        Assert.Equal(PublishProtocolCatalog.DefaultEnabled.ToArray(), protocols.ToArray());
    }

    [Fact]
    public void AiProposal_InfersStyleIntentFromStyledByPhrase()
    {
        Assert.Equal("by zoning", AiPublishProposalSummary.InferStyleIntent("… styled by zoning"));
        Assert.Null(AiPublishProposalSummary.InferStyleIntent("publish it with no styling"));
    }

    [Fact]
    public void AiProposal_Headline_IsOutcomeFirst_NotAPlanDump()
    {
        var headline = AiPublishProposalSummary.Headline("Maui Parcels", ["FeatureServer", "Stac"], "by zoning");

        Assert.Equal("I'll publish Maui Parcels as FeatureServer + STAC API, styled by zoning.", headline);
    }

    // ----------------------------------------------------------------- AI driver flow (bUnit)

    [Fact]
    public async Task AiFlow_Intent_ProducesOutcomeCard_WithPlumbingBehindDetails()
    {
        using var ctx = NewContext();
        RegisterAiServer(ctx, FakeAiPublishDriver.Enabled());

        var cut = ctx.Render<DataToPublishFlow>(p => p.Add(c => c.InitialDriver, "ai"));

        cut.Find("textarea[data-ai-intent]").Input("publish parcels as a feature service and a STAC catalog, styled by zoning");
        await cut.Find("button[data-ai-propose]").ClickAsync(new());

        // The headline is the outcome, not a plan/spec dump.
        Assert.Contains("data-ai-outcome-card", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("I'll publish", cut.Markup, StringComparison.Ordinal);
        // The plan internals are NOT shown until Details is opened (plumbing hidden).
        Assert.DoesNotContain("data-ai-outcome-details", cut.Markup, StringComparison.Ordinal);

        await cut.Find("button[data-ai-details-toggle]").ClickAsync(new());
        Assert.Contains("data-ai-outcome-details", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("data-ai-detail-publications", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AiFlow_Approve_AppliesThroughTheSameWiredPublishPath()
    {
        using var ctx = NewContext();
        var publish = new AiModeRecordingPublishOperation();
        RegisterAiServer(ctx, FakeAiPublishDriver.Enabled(), publish);

        var cut = ctx.Render<DataToPublishFlow>(p => p.Add(c => c.InitialDriver, "ai"));

        cut.Find("textarea[data-ai-intent]").Input("publish parcels as a feature service");
        await cut.Find("button[data-ai-propose]").ClickAsync(new());
        await cut.Find("button[data-ai-approve]").ClickAsync(new());

        // Approve published through IServiceLayerPublishOperation (the SAME wired admin op manual mode uses)
        // and landed the shared Done panel — the human approval is the single gate.
        Assert.True(publish.PublishCalled);
        Assert.Equal("parcels", publish.LastCommand!.Table);
        Assert.Contains("data-step-body=\"Done\"", cut.Markup, StringComparison.Ordinal);
        Assert.Equal("approved", FakeAiPublishDriver.LastDecision);
    }

    [Fact]
    public async Task AiFlow_Edit_HandsOffToManualControls()
    {
        using var ctx = NewContext();
        RegisterAiServer(ctx, FakeAiPublishDriver.Enabled());

        var cut = ctx.Render<DataToPublishFlow>(p => p.Add(c => c.InitialDriver, "ai"));

        cut.Find("textarea[data-ai-intent]").Input("publish parcels as a feature service");
        await cut.Find("button[data-ai-propose]").ClickAsync(new());
        await cut.Find("button[data-ai-edit]").ClickAsync(new());

        // Edit drops the proposal into the manual Publish step (the manual driver surface), pre-filled.
        Assert.Contains("data-step-body=\"Publish\"", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("data-publish-protocol-picker", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("data-ai-outcome-card", cut.Markup, StringComparison.Ordinal);
        Assert.Equal("edited", FakeAiPublishDriver.LastDecision);
    }

    [Fact]
    public async Task AiFlow_Reject_DiscardsTheProposal()
    {
        using var ctx = NewContext();
        RegisterAiServer(ctx, FakeAiPublishDriver.Enabled());

        var cut = ctx.Render<DataToPublishFlow>(p => p.Add(c => c.InitialDriver, "ai"));

        cut.Find("textarea[data-ai-intent]").Input("publish parcels as a feature service");
        await cut.Find("button[data-ai-propose]").ClickAsync(new());
        await cut.Find("button[data-ai-reject]").ClickAsync(new());

        Assert.DoesNotContain("data-ai-outcome-card", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("data-step-body=\"Done\"", cut.Markup, StringComparison.Ordinal);
        Assert.Equal("rejected", FakeAiPublishDriver.LastDecision);
    }

    [Fact]
    public void AiFlow_ServerWithAiOff_SurfacesHonestUnavailableState()
    {
        using var ctx = NewContext();
        RegisterAiServer(ctx, FakeAiPublishDriver.AiOff("AI generation is switched off on this server."));

        var cut = ctx.Render<DataToPublishFlow>(p => p.Add(c => c.InitialDriver, "ai"));

        Assert.Contains("data-ai-unavailable", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("switched off", cut.Markup, StringComparison.Ordinal);
        // No intent box / propose button when AI is off — only the honest state + manual escape hatch.
        Assert.DoesNotContain("data-ai-propose", cut.Markup, StringComparison.Ordinal);
    }

    // ----------------------------------------------------------------- redirects

    // The pure list aliases that have NO standalone page body fold into the unified treeview / flow:
    // OperateDataRedirects owns these routes and rewrites them on init (redesign §5.2). The old entry
    // points that DO have a dedicated flow page keep their own @page ownership instead (see the
    // separate route-ownership test below) — routing them through OperateDataRedirects would steal the
    // route from the owning component (commit "Restore @page directives on orphaned Operate pages").
    [Theory]
    [InlineData("operate/resources", "/operate/data")]
    [InlineData("operate/services", "/operate/data?view=services")]
    [InlineData("operate/resources/new", "/operate/data/new")]
    public void Redirects_FoldOldListAliasesIntoTheUnifiedFlow(string from, string expectedTarget)
    {
        using var ctx = NewContext();
        var nav = ctx.Services.GetRequiredService<Bunit.TestDoubles.BunitNavigationManager>();
        nav.NavigateTo(from);

        ctx.Render<OperateDataRedirects>();

        Assert.EndsWith(expectedTarget, nav.Uri, StringComparison.Ordinal);
    }

    // The old entry points that own a dedicated flow page (import-file, quick-publish, import-service,
    // and the layers list) resolve directly to that page — they are NOT redirected through
    // OperateDataRedirects, so the fold neither dead-ends nor is stolen back by the redirects host.
    // Pin each route to its single owning routable component (same enumeration the live router builds).
    [Theory]
    [InlineData("/operate/resources/import", "OperateImportFilePage")]
    [InlineData("/operate/publishing/quick", "OperatePublishLayerPage")]
    [InlineData("/operate/import/service", "OperateImportServicePage")]
    [InlineData("/operate/layers", "OperateLayersPage")]
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
        ctx.Services.AddSingleton<IOperateTransitionDataSource>(new OneConnectionDataSource("conn1", "Primary"));
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

        var cut = ctx.Render<DataToPublishFlow>(p => p.Add(c => c.InitialSource, "table"));

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

    // Registers an AI-mode harness: a fake AI driver, a one-connection data source, and a publish op that
    // advertises a "parcels" table so a proposal can resolve to a real publishable table and Approve can
    // publish through the SAME wired IServiceLayerPublishOperation path manual mode uses.
    private static void RegisterAiServer(
        BunitContext ctx,
        FakeAiPublishDriver driver,
        IServiceLayerPublishOperation? publish = null)
    {
        FakeAiPublishDriver.LastDecision = null;
        ctx.Services.AddSingleton<IConsoleFileImportOperation>(new UnsupportedConsoleFileImportOperation());
        ctx.Services.AddSingleton<IServiceLayerPublishOperation>(publish ?? new AiModeRecordingPublishOperation());
        ctx.Services.AddSingleton<IStudioMapStyleCatalogDataSource>(new UnsupportedStudioMapStyleCatalogDataSource());
        ctx.Services.AddSingleton<IAiPublishDriver>(driver);
        ctx.Services.AddSingleton<IOperateTransitionDataSource>(new AiModeOneConnectionDataSource());
    }

    /// <summary>A scripted AI driver: enabled-with-proposal, AI-off, or blocked — for the AI-mode flow tests.</summary>
    private sealed class FakeAiPublishDriver : IAiPublishDriver
    {
        public static string? LastDecision;

        private readonly AiPublishCapability _capability;
        private readonly Func<string, IReadOnlyList<AiPublishResourceRef>, AiPublishOutcome> _propose;

        private FakeAiPublishDriver(
            AiPublishCapability capability,
            Func<string, IReadOnlyList<AiPublishResourceRef>, AiPublishOutcome> propose)
        {
            _capability = capability;
            _propose = propose;
        }

        public static FakeAiPublishDriver Enabled() =>
            new(
                new AiPublishCapability(true, "bedrock", null),
                (intent, known) =>
                {
                    var protocols = AiPublishProposalSummary.InferProtocols(intent);
                    var resource = known.FirstOrDefault();
                    return AiPublishOutcome.Proposed(new AiPublishProposal
                    {
                        ResourceName = resource.Name,
                        Protocols = protocols,
                        ServiceSlot = resource.Name,
                        StyleIntent = AiPublishProposalSummary.InferStyleIntent(intent),
                        Rationale = "The server AI confirmed it can plan this publish.",
                        PlannedPublications = PublishProtocolCatalog.PlanPublications(resource.Name, protocols),
                        Provider = "bedrock",
                        FeedbackId = "fb-1",
                    });
                });

        public static FakeAiPublishDriver AiOff(string detail) =>
            new(AiPublishCapability.Off(detail), (_, _) => AiPublishOutcome.Unavailable(detail));

        public Task<AiPublishCapability> GetCapabilityAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_capability);

        public Task<AiPublishOutcome> ProposeAsync(
            string intent,
            IReadOnlyList<AiPublishResourceRef> knownResources,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_propose(intent, knownResources));

        public Task RecordDecisionAsync(string? feedbackId, string action, CancellationToken cancellationToken = default)
        {
            LastDecision = action;
            return Task.CompletedTask;
        }
    }

    /// <summary>A publish op that advertises one "parcels" table and records the publish command on Approve.</summary>
    private sealed class AiModeRecordingPublishOperation : IServiceLayerPublishOperation
    {
        public bool PublishCalled { get; private set; }
        public ServiceLayerPublishCommand? LastCommand { get; private set; }

        public Task<ServiceLayerPublishResult> PublishAsync(
            ServiceLayerPublishCommand command,
            CancellationToken cancellationToken = default)
        {
            PublishCalled = true;
            LastCommand = command;
            return Task.FromResult(new ServiceLayerPublishResult
            {
                Succeeded = true,
                State = "Published",
                Detail = "Published",
                LayerName = command.LayerName,
            });
        }

        public Task<ServiceProtocolEnableResult> EnableProtocolsAsync(
            string serviceName,
            IReadOnlyList<string> protocols,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ServiceProtocolEnableResult
            {
                Succeeded = true,
                State = "Published",
                EnabledProtocols = protocols,
            });

        public Task<IReadOnlyList<ServiceLayerPublishTable>> ListTablesAsync(
            string connectionId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ServiceLayerPublishTable>>(
            [
                new ServiceLayerPublishTable
                {
                    Schema = "public",
                    Table = "parcels",
                    GeometryColumn = "geom",
                    GeometryType = "Polygon",
                    Srid = 4326,
                    Columns = ["objectid", "zoning"],
                },
            ]);
    }

    /// <summary>A data source advertising one connection so AI mode can load publishable tables.</summary>
    private sealed class AiModeOneConnectionDataSource : IOperateTransitionDataSource
    {
        private static readonly OperateConnectionSummary Connection =
            new("conn1", "PostGIS", "postgis", "db", "app", "Connected", "now", null);

        public Task<OperateTransitionWorkspace> GetWorkspaceAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new OperateTransitionWorkspace([Connection], [], [], [], []));

        public Task<OperateConnectionSummary?> FindConnectionAsync(string connectionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<OperateConnectionSummary?>(Connection);

        public Task<OperateResourceEditPreview?> FindResourceEditAsync(string resourceId, CancellationToken cancellationToken = default) =>
            Task.FromResult<OperateResourceEditPreview?>(null);

        public Task<OperateServiceDetail?> FindServiceAsync(string serviceName, CancellationToken cancellationToken = default) =>
            Task.FromResult<OperateServiceDetail?>(null);
    }

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

    private sealed class WorkspaceDataSource(OperateTransitionWorkspace workspace) : IOperateTransitionDataSource
    {
        public Task<OperateTransitionWorkspace> GetWorkspaceAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(workspace);

        public Task<OperateConnectionSummary?> FindConnectionAsync(
            string connectionId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<OperateConnectionSummary?>(null);

        public Task<OperateResourceEditPreview?> FindResourceEditAsync(
            string resourceId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<OperateResourceEditPreview?>(null);

        public Task<OperateServiceDetail?> FindServiceAsync(
            string serviceName,
            CancellationToken cancellationToken = default) =>
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
