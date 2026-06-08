using Bunit;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free render + interaction coverage for the functional publish wizards
/// (<see cref="PublishWizardWorkspace"/>) wired into the Operate publishing workspace. The wizard binds
/// its source / service / resource pickers to REAL server state (the Operate transition data source +
/// the service-layer-publish operation's table listing); these tests prove the bodies render that live
/// data, the stepper navigation is FUNCTIONAL (forward/back move between real step bodies with per-step
/// validity gating), and — per the no-mocks rule (Console Patterns Charter section 11) — that with no
/// server configured each section renders an honest empty / "select a source" state instead of
/// fabricated rows.
/// </summary>
public sealed class PublishWizardWorkspaceRenderTests
{
    // ---- Stubs returning real-SHAPED data (no server). The wizard treats these exactly as it would a
    //      live honua-server projection; the data is the test's, not fabricated inside the component. ----

    private sealed class StubOperateData : IOperateTransitionDataSource
    {
        private readonly OperateTransitionWorkspace _workspace;
        public StubOperateData(OperateTransitionWorkspace workspace) => _workspace = workspace;

        public Task<OperateTransitionWorkspace> GetWorkspaceAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_workspace);

        public Task<OperateConnectionSummary?> FindConnectionAsync(string connectionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_workspace.Connections.FirstOrDefault(c => c.Id == connectionId));

        public Task<OperateResourceEditPreview?> FindResourceEditAsync(string resourceId, CancellationToken cancellationToken = default) =>
            Task.FromResult<OperateResourceEditPreview?>(null);

        public Task<OperateServiceDetail?> FindServiceAsync(string serviceName, CancellationToken cancellationToken = default) =>
            Task.FromResult(_workspace.Services.FirstOrDefault(s => s.Name == serviceName));
    }

    private sealed class StubPublishOperation : IServiceLayerPublishOperation
    {
        private readonly IReadOnlyList<ServiceLayerPublishTable> _tables;
        public ServiceLayerPublishCommand? LastCommand { get; private set; }

        public StubPublishOperation(IReadOnlyList<ServiceLayerPublishTable> tables) => _tables = tables;

        public Task<ServiceLayerPublishResult> PublishAsync(ServiceLayerPublishCommand command, CancellationToken cancellationToken = default)
        {
            LastCommand = command;
            return Task.FromResult(new ServiceLayerPublishResult
            {
                Succeeded = true,
                State = "Published",
                LayerId = 1,
                LayerName = command.LayerName,
                ServiceName = command.ServiceName,
                GeometryType = command.GeometryType,
                Srid = command.Srid,
                Enabled = true
            });
        }

        public Task<IReadOnlyList<ServiceLayerPublishTable>> ListTablesAsync(string connectionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_tables);
    }

    private static readonly OperateServiceDetail SampleService = new(
        Name: "demo-fs",
        DisplayName: "Demo Feature Service",
        ServiceType: "FeatureServer",
        RuntimeStatus: "running",
        MetadataOwnership: "server",
        Layers: [new OperateServiceLayerProjection(0, "existing", "Polygon", "res-0", "existing-resource")],
        RuntimeSettings: [],
        PublicationSlots: []);

    private static readonly OperateConnectionSummary SampleConnection = new(
        Id: "conn-1",
        Name: "demo-postgis",
        Provider: "PostgreSQL",
        Target: "db",
        Principal: "svc",
        Status: "healthy",
        LastTested: "now",
        LastDiagnostic: null);

    private static readonly ServiceLayerPublishTable SampleTable = new()
    {
        Schema = "public",
        Table = "parcels",
        GeometryColumn = "geom",
        GeometryType = "Polygon",
        Srid = 4326,
        EstimatedRows = 1234,
        Columns = ["gid", "name", "area"]
    };

    // Context wired to real-shaped data: one connection (with one publishable table) + one service.
    private static Bunit.TestContext LiveContext()
    {
        var ctx = new Bunit.TestContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        var workspace = new OperateTransitionWorkspace(
            Connections: [SampleConnection],
            ResourceEdits: [],
            Services: [SampleService],
            SettingsChanges: [],
            CapabilityStates: []);
        ctx.Services.AddSingleton<IOperateTransitionDataSource>(new StubOperateData(workspace));
        ctx.Services.AddSingleton<IServiceLayerPublishOperation>(new StubPublishOperation([SampleTable]));
        return ctx;
    }

    // Context with NO server configured: the unsupported data source / publish operation return empty.
    private static Bunit.TestContext EmptyContext()
    {
        var ctx = new Bunit.TestContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IOperateTransitionDataSource>(new UnsupportedOperateTransitionDataSource());
        ctx.Services.AddSingleton<IServiceLayerPublishOperation>(new UnsupportedServiceLayerPublishOperation());
        return ctx;
    }

    private static void SelectSource(IRenderedComponent<PublishWizardWorkspace> cut)
    {
        cut.Find("select[aria-label=\"Source connection\"]").Change(SampleConnection.Id);
        cut.Find("select[aria-label=\"Source table\"]").Change(SampleTable.QualifiedName);
    }

    [Fact]
    public void QuickFlow_StartsOnServiceStep_BindingTheLiveServiceTree()
    {
        using var ctx = LiveContext();
        var cut = ctx.RenderComponent<PublishWizardWorkspace>();

        // Quick mode is the default and its stepper reads Service → Layer → Review.
        var stepper = cut.Find("ol.publish-stepper");
        Assert.Contains("Service", stepper.TextContent, StringComparison.Ordinal);
        Assert.Contains("Layer", stepper.TextContent, StringComparison.Ordinal);
        Assert.Contains("Review", stepper.TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("Projection", stepper.TextContent, StringComparison.Ordinal);

        // The service step renders the use-existing / create-new segment and the LIVE service tree.
        Assert.Equal(2, cut.FindAll("[data-quick-step=\"service\"] .publish-segment-option").Count);
        var tree = cut.Find("[data-publish-tree]");
        Assert.Contains("Demo Feature Service", tree.TextContent, StringComparison.Ordinal);
        Assert.Contains("FeatureServer", tree.TextContent, StringComparison.Ordinal);

        // No fabricated parcels/prod-postgis scaffolding leaks into the markup.
        Assert.DoesNotContain("parcels_2024", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("prod-postgis", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void QuickFlow_NoServer_RendersHonestEmptySourceAndServiceState()
    {
        using var ctx = EmptyContext();
        var cut = ctx.RenderComponent<PublishWizardWorkspace>();

        // The source picker shows the honest "add a connection" empty state (no fabricated source).
        Assert.NotNull(cut.Find("[data-source-empty]"));
        // The service tree renders its honest empty state, not six mock rows.
        Assert.NotNull(cut.Find("[data-publish-tree-empty]"));
        Assert.Empty(cut.FindAll("[data-publish-tree]"));
    }

    [Fact]
    public void QuickFlow_CreateNewService_SwapsToEditableNewServiceCards()
    {
        using var ctx = LiveContext();
        var cut = ctx.RenderComponent<PublishWizardWorkspace>();

        var createNew = cut.FindAll("[data-quick-step=\"service\"] .publish-segment-option")
            .Single(b => b.TextContent.Contains("Create new service", StringComparison.Ordinal));
        createNew.Click();

        // New-service cards appear with a REAL editable name input (not a readonly mock value).
        var identity = cut.Find("[data-new-service=\"identity\"]");
        var nameInput = identity.QuerySelector("input.console-input");
        Assert.NotNull(nameInput);
        Assert.False(nameInput!.HasAttribute("readonly"));
        Assert.NotNull(cut.Find("[data-new-service=\"catalog\"]"));

        // The rail swaps to "What gets created".
        Assert.Contains("What gets created", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void QuickFlow_SelectingSourceBindsResourceTableToLiveTables()
    {
        using var ctx = LiveContext();
        var cut = ctx.RenderComponent<PublishWizardWorkspace>();

        // Pick a service (gates the Service step) and a source connection + table.
        cut.Find("[data-publish-tree] .publish-tree-row").Click();
        SelectSource(cut);

        // Advance to the Layer step; the resource table now lists the live table from the connection.
        cut.Find(".publish-wizard-next").Click();
        Assert.Contains("Layer", cut.Find(".publish-step-current").TextContent, StringComparison.Ordinal);
        var table = cut.Find("[data-resource-table]");
        Assert.Contains("public.parcels", table.TextContent, StringComparison.Ordinal);
        Assert.Contains("Polygon", table.TextContent, StringComparison.Ordinal);
        // No fabricated PII / feature-count columns.
        Assert.DoesNotContain("PII", table.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void QuickFlow_ResourceActions_AreWiredToRealNavigation()
    {
        using var ctx = LiveContext();
        var nav = ctx.Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        var cut = ctx.RenderComponent<PublishWizardWorkspace>();
        cut.Find("[data-publish-tree] .publish-tree-row").Click();
        SelectSource(cut);
        cut.Find(".publish-wizard-next").Click();

        // "Create from connection" navigates to the real connection page (no dead button).
        var actions = cut.Find(".publish-resource-actions");
        var createFromConnection = actions.QuerySelectorAll("button")
            .Single(b => b.TextContent.Contains("Create from connection", StringComparison.Ordinal));
        createFromConnection.Click();
        Assert.EndsWith("/operate/connections/new", nav.Uri, StringComparison.Ordinal);

        // The dead "Migrate remote service" button is gone entirely.
        Assert.DoesNotContain("Migrate remote service", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void QuickFlow_ReviewStep_PublishesTheRealSelectedSourceAndService()
    {
        using var ctx = LiveContext();
        var publishOp = (StubPublishOperation)ctx.Services.GetRequiredService<IServiceLayerPublishOperation>();
        var cut = ctx.RenderComponent<PublishWizardWorkspace>();

        SelectSource(cut);
        // The pre-selected service comes from the tree; select it explicitly to be safe.
        cut.Find("[data-publish-tree] .publish-tree-row").Click();

        // Walk Service -> Layer -> Review.
        cut.Find(".publish-wizard-next").Click();
        cut.Find(".publish-wizard-next").Click();
        Assert.Contains("Review", cut.Find(".publish-step-current").TextContent, StringComparison.Ordinal);

        // Finish publishes a command built from the REAL selected table + service (not constants).
        var finish = cut.Find(".publish-wizard-finish");
        finish.Click();
        Assert.NotNull(cut.Find("[data-publish-result]"));
        Assert.NotNull(publishOp.LastCommand);
        Assert.Equal("conn-1", publishOp.LastCommand!.ConnectionId);
        Assert.Equal("public", publishOp.LastCommand.Schema);
        Assert.Equal("parcels", publishOp.LastCommand.Table);
        Assert.Equal("demo-fs", publishOp.LastCommand.ServiceName);
        Assert.Equal(4326, publishOp.LastCommand.Srid);
    }

    [Fact]
    public void QuickFlow_FinishWithoutSource_ShowsMissingBindingNotFabricatedPublish()
    {
        using var ctx = LiveContext();
        var cut = ctx.RenderComponent<PublishWizardWorkspace>();

        // Select a service but NO source table, then walk to Review and finish.
        cut.Find("[data-publish-tree] .publish-tree-row").Click();
        // Layer step is gated (no source table), so the forward control is disabled there; drive the
        // finish path directly is not possible past the gate — assert the gate instead.
        cut.Find(".publish-wizard-next").Click(); // -> Layer
        var next = cut.Find(".publish-wizard-next");
        Assert.True(next.HasAttribute("disabled"));
    }

    [Fact]
    public void ModeToggle_SwapsToAuthorFirstSevenStepFlow_BindingLiveContext()
    {
        using var ctx = LiveContext();
        var cut = ctx.RenderComponent<PublishWizardWorkspace>();

        cut.FindAll("button.publish-mode-option")
            .Single(b => b.TextContent.Contains("Author resource first", StringComparison.Ordinal))
            .Click();

        var stepper = cut.Find("ol.publish-stepper");
        Assert.Contains("Target", stepper.TextContent, StringComparison.Ordinal);
        Assert.Contains("Compatibility", stepper.TextContent, StringComparison.Ordinal);
        Assert.Contains("Projection", stepper.TextContent, StringComparison.Ordinal);
        Assert.Contains("Access", stepper.TextContent, StringComparison.Ordinal);

        // Target step renders the resource context bar and the LIVE service tree.
        Assert.NotNull(cut.Find("[data-author-context]"));
        var tree = cut.Find("[data-author-step=\"target\"] [data-publish-tree]");
        Assert.Contains("Demo Feature Service", tree.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void AuthorFirst_TargetGating_EnablesForwardWhenTargetSelected()
    {
        using var ctx = LiveContext();
        var cut = ctx.RenderComponent<PublishWizardWorkspace>();

        cut.FindAll("button.publish-mode-option")
            .Single(b => b.TextContent.Contains("Author resource first", StringComparison.Ordinal))
            .Click();

        // No target is pre-selected now (no fabricated default), so forward is gated until one is picked.
        Assert.True(cut.Find(".publish-wizard-next").HasAttribute("disabled"));

        cut.Find("[data-author-step=\"target\"] [data-publish-tree] .publish-tree-row").Click();
        var next = cut.Find(".publish-wizard-next");
        Assert.False(next.HasAttribute("disabled"));
        Assert.Contains("Continue · Compatibility →", next.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void AuthorFirst_ProjectionStep_RendersLiveMapPreviewWithLegendAndChips()
    {
        using var ctx = LiveContext();
        var cut = ctx.RenderComponent<PublishWizardWorkspace>();

        cut.FindAll("button.publish-mode-option")
            .Single(b => b.TextContent.Contains("Author resource first", StringComparison.Ordinal))
            .Click();
        cut.Find("[data-author-step=\"target\"] [data-publish-tree] .publish-tree-row").Click();

        // Advance Target -> Compatibility -> Slot -> Fields -> Projection (4 forward clicks).
        for (var i = 0; i < 4; i++)
        {
            cut.Find(".publish-wizard-next").Click();
        }

        Assert.Contains("Projection", cut.Find(".publish-step-current").TextContent, StringComparison.Ordinal);

        Assert.Contains("map-preview-schematic", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Class breaks", cut.Markup, StringComparison.Ordinal);
        Assert.True(cut.FindAll(".map-preview-basemap-chip").Count >= 2);

        var controls = cut.Find("[data-projection-controls]");
        Assert.Contains("labels", controls.TextContent, StringComparison.Ordinal);
        Assert.Contains("popups", controls.TextContent, StringComparison.Ordinal);
        Assert.Contains("highlight", controls.TextContent, StringComparison.Ordinal);
        Assert.Contains("This slot", cut.Markup, StringComparison.Ordinal);

        // The fabricated "1k sampled / 1.28M" sentence is gone.
        Assert.DoesNotContain("1.28M", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("sampled features", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void AuthorFirst_ProjectionLabelChip_TogglesLabelLandmark()
    {
        using var ctx = LiveContext();
        var cut = ctx.RenderComponent<PublishWizardWorkspace>();

        cut.FindAll("button.publish-mode-option")
            .Single(b => b.TextContent.Contains("Author resource first", StringComparison.Ordinal))
            .Click();
        cut.Find("[data-author-step=\"target\"] [data-publish-tree] .publish-tree-row").Click();
        for (var i = 0; i < 4; i++)
        {
            cut.Find(".publish-wizard-next").Click();
        }

        Assert.Contains("map-preview-labels", cut.Markup, StringComparison.Ordinal);

        var labelsChip = cut.FindAll("[data-projection-controls] .publish-filter-chip")
            .Single(b => b.TextContent.Trim() == "labels");
        Assert.Contains("publish-filter-chip-on", labelsChip.ClassName!, StringComparison.Ordinal);
        labelsChip.Click();
        Assert.DoesNotContain("map-preview-labels", cut.Markup, StringComparison.Ordinal);
    }
}
