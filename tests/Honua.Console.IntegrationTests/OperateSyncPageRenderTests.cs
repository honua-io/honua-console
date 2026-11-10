using Bunit;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class OperateSyncPageRenderTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Sync_WhenBindingMissing_RendersNotBoundSurfaceNotEmptyQueue(bool enabled)
    {
        var page = Render(new FakeSyncClient
        {
            Workspace = new TemporalViewerWorkspace([], [MissingBindingState])
        }, enabled);

        page.WaitForAssertion(
            () => Assert.Contains("Sync conflict review is not bound", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Equal(enabled ? 1 : 0, page.FindAll(".console-page-heading-v4").Count);
        if (enabled) CatalogAdminPreview.Capture("sync-missing", page.Markup);
        Assert.Contains("honua-server#1167", page.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("<table", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Sync_MergedBuildPage_RendersMissingBindingThroughRealDi()
    {
        using var ctx = new Bunit.BunitContext();
        ctx.AddConsoleNotifications();
        ctx.Services.AddSingleton<ITemporalCapabilityClient, UnsupportedTemporalCapabilityClient>();
        ctx.Services.AddSingleton(ConsoleCapabilityTestManifest.All);

        var page = ctx.Render<OperateSyncPage>();

        page.WaitForAssertion(
            () => Assert.Contains("Sync conflict review is not bound", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.DoesNotContain("<table", page.Markup, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Sync_WhenSyncSourceBound_ListsReplicaQueueAndResolvesConflict(bool enabled)
    {
        var client = new FakeSyncClient
        {
            Workspace = new TemporalViewerWorkspace([SyncSource], []),
            ReplicaQueue = new ReplicaConflictQueue([SampleReplica]),
            ConflictReview = new ReplicaConflictReview(SampleReplica,
            [
                new SyncConflict("conflict-1", "replica-1", "src-parcels", "0", "f-1",
                    SyncConflictType.Attribute, "r-base", "r-server",
                    [new SyncFieldConflict("owner_name", "Acme", "Acme LLC", "Acme Inc")],
                    GeometryConflict: true, DateTimeOffset.UtcNow, SyncConflictStatus.Pending),
            ]),
            Resolution = new SyncConflictResolutionResult(
                "res-1", ["conflict-1"], SyncResolutionAction.AcceptClient,
                ResultRevisionId: "rev-55", ResultChangeSetId: "cs-9",
                AuditEventIds: ["audit-1"], JobRunId: "job-3"),
            ReviewAfterResolution = new ReplicaConflictReview(SampleReplica, [])
        };
        var page = Render(client, enabled);
        Assert.Equal(enabled ? "Sync" : "Disconnected sync review", page.Find("h1").TextContent);
        if (enabled) CatalogAdminPreview.Capture("sync-list", page.Markup);

        page.WaitForAssertion(() => Assert.Contains("Open queue", page.Markup, StringComparison.Ordinal), TimeSpan.FromSeconds(5));
        ClickByText(page, "Open queue");
        page.WaitForAssertion(() => Assert.Contains("Field Crew 7", page.Markup, StringComparison.Ordinal), TimeSpan.FromSeconds(5));
        Assert.Contains(enabled ? "Replica conflict queue" : "Replica Conflict Queue", page.Markup, StringComparison.Ordinal);
        if (enabled) CatalogAdminPreview.Capture("sync-queue", page.Markup);

        ClickByText(page, "Review");
        page.WaitForAssertion(() => Assert.Contains(enabled ? "Conflict comparison" : "Base / Client / Server Comparison", page.Markup, StringComparison.Ordinal), TimeSpan.FromSeconds(5));
        Assert.Contains("owner_name", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Geometry conflict", page.Markup, StringComparison.Ordinal);

        if (enabled) CatalogAdminPreview.Capture("sync-conflicts", page.Markup);
        ClickByText(page, "Client wins");
        page.WaitForAssertion(() => Assert.Contains("Committed Change Set", page.Markup, StringComparison.Ordinal), TimeSpan.FromSeconds(5));
        Assert.Contains("cs-9", page.Markup, StringComparison.Ordinal);
        Assert.Contains("audit-1", page.Markup, StringComparison.Ordinal);
        Assert.Equal("/console/operate/jobs/job-3", page.Find("[data-sync-region=committed] a").GetAttribute("href"));
        if (enabled) CatalogAdminPreview.Capture("sync-resolved", page.Markup);
    }

    [Theory]
    [InlineData("cadastre/0")]
    [InlineData("CADASTRE/0")]
    public async Task Sync_DeepLinkLoadsReplicaAndPublishesSelectionAndUnsavedSummary(string sourceId)
    {
        using var ctx = new BunitContext();
        ctx.AddConsoleNotifications();
        ctx.Services.AddSingleton(new ConsoleV4Options { Enabled = true });
        ctx.Services.AddSingleton<ConsoleAgentContextRegistry>();
        ctx.Services.AddSingleton(ConsoleCapabilityTestManifest.All);
        var client = DeepLinkClient();
        client.Workspace = new TemporalViewerWorkspace([SyncSource with { SourceId = sourceId }], []);
        ctx.Services.AddSingleton<ITemporalCapabilityClient>(client);
        ctx.Services.AddSingleton<IOperateTransitionDataSource>(new SyncLayerDataSource());
        Navigate(ctx, sourceId, "replica-1");
        var page = ctx.Render<OperateSyncPage>();
        page.WaitForAssertion(() => Assert.Single(page.FindAll("[data-sync-conflict]")));
        var registry = ctx.Services.GetRequiredService<ConsoleAgentContextRegistry>();
        Assert.Equal("/operate/sync", registry.Current.Route);
        Assert.Equal(sourceId, registry.Current.Filters["sourceId"]);
        Assert.Equal("replica-1", registry.Current.Filters["replicaId"]);
        var selected = Assert.Single(registry.Current.Selection);
        Assert.Equal(ConsoleAgentEntityKind.Layer, selected.Kind);
        Assert.Equal("0", selected.Id);
        Assert.Equal("conn-a-layer-0", selected.ResourceId);
        Assert.Equal("/operate/layers/conn-a-layer-0", ConsoleAgentRoutes.ForEntity(selected));
        page.Find("[data-sync-conflict] input[type=checkbox]").Change(true);
        Assert.Equal(1, registry.Current.Unsaved.ChangedFieldCount);
        Assert.Equal("conflict-1", registry.Current.Filters["conflictIds"]);
        page.Find("select[aria-label='Batch action']").Change("AcceptClient");
        Assert.Equal(2, registry.Current.Unsaved.ChangedFieldCount);
        await ctx.DisposeComponentsAsync();
        Assert.Empty(registry.Current.Selection);
        Assert.Empty(registry.Current.Filters);
    }

    [Theory]
    [InlineData("cadastre/0", null, true)]
    [InlineData("cadastre/0", "unknown-replica", true)]
    [InlineData(null, null, false)]
    [InlineData("unknown-source", "replica-1", false)]
    public void Sync_ChangedDeepLinkClearsStaleReview(string? sourceId, string? replicaId, bool keepsSource)
    {
        using var ctx = new BunitContext();
        ctx.AddConsoleNotifications();
        ctx.Services.AddSingleton(new ConsoleV4Options { Enabled = true });
        ctx.Services.AddSingleton<ConsoleAgentContextRegistry>();
        ctx.Services.AddSingleton(ConsoleCapabilityTestManifest.All);
        ctx.Services.AddSingleton<ITemporalCapabilityClient>(DeepLinkClient());
        ctx.Services.AddSingleton<IOperateTransitionDataSource>(new SyncLayerDataSource());
        Navigate(ctx, "cadastre/0", "replica-1");
        var page = ctx.Render<OperateSyncPage>();
        page.Find("[data-sync-conflict] input[type=checkbox]").Change(true);
        page.Find("select[aria-label='Batch action']").Change("AcceptClient");

        Navigate(ctx, sourceId, replicaId);

        Assert.Empty(page.FindAll("[data-sync-region='conflict-merge']"));
        Assert.Equal(keepsSource ? 1 : 0, page.FindAll("[data-sync-region='replica-queue']").Count);
        var snapshot = ctx.Services.GetRequiredService<ConsoleAgentContextRegistry>().Current;
        Assert.False(snapshot.Filters.ContainsKey("replicaId"));
        Assert.False(snapshot.Filters.ContainsKey("conflictIds"));
        Assert.Equal(0, snapshot.Unsaved.ChangedFieldCount);
        Assert.Equal(keepsSource ? 1 : 0, snapshot.Selection.Count);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Sync_UnchangedQueryPreservesManualReviewAndConflictSelections(bool enabled, bool sourceQuery)
    {
        using var ctx = NewContext(DeepLinkClient(), enabled);
        var page = ctx.Render<OperateSyncPage>();
        var sourceId = sourceQuery ? "cadastre/0" : null;
        Navigate(ctx, sourceId);
        ClickByText(page, "Open queue");
        ClickByText(page, "Review");
        page.Find("[data-sync-conflict] input[type=checkbox]").Change(true);
        page.Find("select[aria-label='Batch action']").Change("AcceptClient");

        Navigate(ctx, sourceId);
        page.Render();

        Assert.Single(page.FindAll("[data-sync-region='conflict-merge']"));
        Assert.True(page.Find("[data-sync-conflict] input[type=checkbox]").HasAttribute("checked"));
        Assert.Equal("AcceptClient", page.Find("select[aria-label='Batch action']").GetAttribute("value"));
    }

    [Fact]
    public async Task Sync_LeavingReviewWhileResolutionPendingDoesNotPublishStaleResults()
    {
        var completion = new TaskCompletionSource<SyncConflictResolutionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = DeepLinkClient();
        client.PendingResolution = completion.Task;
        using var ctx = new BunitContext();
        ctx.AddConsoleNotifications();
        ctx.Services.AddSingleton(new ConsoleV4Options { Enabled = true });
        ctx.Services.AddSingleton<ConsoleAgentContextRegistry>();
        ctx.Services.AddSingleton<ITemporalCapabilityClient>(client);
        ctx.Services.AddSingleton(ConsoleCapabilityTestManifest.All);
        Navigate(ctx, "cadastre/0", "replica-1");
        var page = ctx.Render<OperateSyncPage>();
        var action = page.InvokeAsync(() => page.FindAll("button").Single(button => button.TextContent.Trim() == "Client wins").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs()));
        page.WaitForAssertion(() => Assert.True(client.ResolutionStarted));
        Navigate(ctx, null);
        completion.SetResult(new SyncConflictResolutionResult("res-1", ["conflict-1"], SyncResolutionAction.AcceptClient,
            ResultRevisionId: "rev-55", ResultChangeSetId: "cs-9", AuditEventIds: ["audit-1"], JobRunId: "job-3"));
        await action;
        Assert.Empty(page.FindAll("[data-sync-region=committed]"));
        Assert.Empty(page.FindAll("[data-sync-conflict]"));
        Assert.DoesNotContain(ctx.Services.GetRequiredService<IConsoleNotificationService>().Notifications,
            notification => notification.Level == ConsoleNotificationLevel.Error);
        Assert.Equal(1, client.ReviewCalls);
        var snapshot = page.FindComponent<ConsoleAgentContext>().Instance.Context;
        Assert.Empty(snapshot.Selection);
        Assert.Equal(0, snapshot.Unsaved.PendingOperationCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Sync_OverlappingSourceLoadsKeepTheLatestQueue(bool manualSelection)
    {
        var first = new TaskCompletionSource<ReplicaConflictQueue>(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource<ReplicaConflictQueue>(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = DeepLinkClient();
        client.Workspace = new TemporalViewerWorkspace([
            SyncSource with { SourceId = "cadastre/0" }, SyncSource with { SourceId = "transport/0" }
        ], []);
        client.PendingQueues["cadastre/0"] = first.Task;
        client.PendingQueues["transport/0"] = second.Task;
        using var ctx = NewContext(client, enabled: true);
        var page = ctx.Render<OperateSyncPage>();
        Navigate(ctx, "cadastre/0");
        if (manualSelection)
        {
            _ = page.InvokeAsync(() => page.FindAll("button").Where(button => button.TextContent == "Open queue").Last().ClickAsync(new()));
        }
        else
        {
            Navigate(ctx, "transport/0");
        }
        var renderCount = page.RenderCount;
        first.SetResult(new ReplicaConflictQueue([SampleReplica with { ReplicaId = "stale-replica" }]));
        page.WaitForState(() => page.RenderCount > renderCount);
        second.SetResult(new ReplicaConflictQueue([SampleReplica with { ReplicaId = "current-replica", SourceId = "transport/0" }]));
        page.WaitForAssertion(() => Assert.Contains("current-replica", page.Markup, StringComparison.Ordinal));
        Assert.DoesNotContain("stale-replica", page.Markup, StringComparison.Ordinal);
        Assert.Equal("transport/0", page.FindComponent<ConsoleAgentContext>().Instance.Context.Filters["sourceId"]);
    }

    [Fact]
    public void Sync_UnresolvedLayerDoesNotPublishDisplayNameAsEntityIdentity()
    {
        using var ctx = NewContext(DeepLinkClient(), enabled: true);
        var page = ctx.Render<OperateSyncPage>();
        Navigate(ctx, "cadastre/0");
        var context = page.FindComponent<ConsoleAgentContext>();
        var snapshot = context.Instance.Context;
        Assert.Empty(snapshot.Entities);
        Assert.Empty(snapshot.Selection);
        Assert.Equal("cadastre/0", snapshot.Filters["sourceId"]);
    }

    private static FakeSyncClient DeepLinkClient() => new()
    {
        Workspace = new TemporalViewerWorkspace([SyncSource with { SourceId = "cadastre/0" }], []),
        ReplicaQueue = new ReplicaConflictQueue([SampleReplica with { SourceId = "cadastre/0" }]),
        ConflictReview = new ReplicaConflictReview(SampleReplica,
        [
            new SyncConflict("conflict-1", "replica-1", "cadastre/0", "0", "f-1",
                SyncConflictType.Attribute, "r-base", "r-server", [],
                GeometryConflict: false, DateTimeOffset.UtcNow, SyncConflictStatus.Pending)
        ])
    };

    private sealed class SyncLayerDataSource : IOperateTransitionDataSource
    {
        public Task<OperateTransitionWorkspace> GetWorkspaceAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new OperateTransitionWorkspace([], [],
            [
                new("cadastre", "Cadastre", "FeatureServer", "Running", "honua-server",
                    [new(0, "Parcels", "Polygon", "conn-a-layer-0", "parcels")], [], []),
                new("transport", "Transport", "FeatureServer", "Running", "honua-server",
                    [new(0, "Roads", "LineString", "conn-b-layer-0", "roads")], [], [])
            ], [], []));
        public Task<OperateConnectionSummary?> FindConnectionAsync(string id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OperateResourceEditPreview?> FindResourceEditAsync(string id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OperateServiceDetail?> FindServiceAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private static IRenderedComponent<OperateSyncPage> Render(ITemporalCapabilityClient client, bool enabled = false)
    {
        return NewContext(client, enabled).Render<OperateSyncPage>();
    }

    private static BunitContext NewContext(ITemporalCapabilityClient client, bool enabled)
    {
        var ctx = new BunitContext();
        ctx.AddConsoleNotifications();
        ctx.Services.AddSingleton(new ConsoleV4Options { Enabled = enabled });
        ctx.Services.AddSingleton<ConsoleAgentContextRegistry>();
        ctx.Services.AddSingleton(client);
        ctx.Services.AddSingleton(ConsoleCapabilityTestManifest.All);
        ctx.Services.AddSingleton<NavigationManager>(new HostedNavigationManager());
        return ctx;
    }

    private static void Navigate(BunitContext ctx, string? sourceId, string? replicaId = null)
    {
        var navigation = ctx.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo(navigation.GetUriWithQueryParameters(new Dictionary<string, object?>
        {
            ["sourceId"] = sourceId,
            ["replicaId"] = replicaId
        }));
    }

    private sealed class HostedNavigationManager : NavigationManager
    {
        public HostedNavigationManager() => Initialize("http://localhost/console/", "http://localhost/console/operate/sync");
        protected override void NavigateToCore(string uri, bool forceLoad)
        {
            Uri = ToAbsoluteUri(uri).ToString();
            NotifyLocationChanged(false);
        }
    }

    private static void ClickByText(IRenderedComponent<OperateSyncPage> page, string text) =>
        page.FindAll("button").First(node => node.TextContent.Contains(text, StringComparison.Ordinal)).Click();

    private static readonly TemporalSourceCapability SyncSource = new(
        "src-parcels", "parcels", "0", TemporalMode.History, TemporalSyncCapability.Bidirectional,
        RollbackSupported: false, SyncConflictReviewSupported: true, RetentionPolicyId: "retain-7y")
    {
        HistoryReadPermitted = true,
    };

    private static readonly DisconnectedReplica SampleReplica = new(
        "replica-1", "Field Crew 7", "src-parcels", "owner-42", "device-9",
        TemporalSyncCapability.Bidirectional, "cp-base", ReplicaServerGen: 42,
        LastSyncAt: DateTimeOffset.UtcNow, ReplicaStatus.Conflicted, PendingConflictCount: 1);

    private static readonly TemporalCapabilityState MissingBindingState = new(
        "Disconnected sync review", "Missing binding", "honua-server#1167",
        "Disconnected sync conflict review binds to honua-server#1167.");

    private static readonly TemporalBindingState MissingBinding = new(
        "Disconnected sync review", TemporalBindingState.MissingBinding, "honua-server#1167",
        "Configure Honua:Server:BaseUrl and wait for honua-server#1167.");

    private sealed class FakeSyncClient : ITemporalCapabilityClient
    {
        public TemporalViewerWorkspace Workspace { get; set; } = new([], []);
        public ReplicaConflictQueue ReplicaQueue { get; set; } = new([]);
        public ReplicaConflictReview ConflictReview { get; set; } = new(Replica: null, []);
        public ReplicaConflictReview? ReviewAfterResolution { get; set; }
        public SyncConflictResolutionResult? Resolution { get; set; }
        public Task<SyncConflictResolutionResult>? PendingResolution { get; set; }
        public bool ResolutionStarted { get; private set; }
        public int ReviewCalls { get; private set; }
        public Dictionary<string, Task<ReplicaConflictQueue>> PendingQueues { get; } = [];

        private bool _resolved;

        public Task<TemporalViewerWorkspace> GetWorkspaceAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Workspace);

        public Task<TemporalCheckpointList> GetCheckpointsAsync(string sourceId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new TemporalCheckpointList([], MissingBinding));

        public Task<TemporalDiff> GetDiffAsync(string sourceId, string fromCheckpointId, string toCheckpointId, CancellationToken cancellationToken = default) =>
            Task.FromResult(TemporalDiff.Blocked(MissingBinding));

        public Task<TemporalFeatureTimeline> GetFeatureTimelineAsync(string sourceId, string featureId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new TemporalFeatureTimeline(featureId, sourceId, [], MissingBinding));

        public Task<TemporalRollbackPlan> CreateRollbackPlanAsync(string sourceId, TemporalRollbackScope scope, string targetCheckpointId, CancellationToken cancellationToken = default) =>
            Task.FromResult(TemporalRollbackPlan.Blocked(MissingBinding));

        public Task<TemporalRollbackOperation> ExecuteRollbackAsync(string rollbackPlanId, CancellationToken cancellationToken = default) =>
            Task.FromResult(TemporalRollbackOperation.Blocked(MissingBinding));

        public Task<ReplicaConflictQueue> GetReplicaConflictQueueAsync(string sourceId, CancellationToken cancellationToken = default) =>
            PendingQueues.TryGetValue(sourceId, out var pending) ? pending : Task.FromResult(ReplicaQueue);

        public Task<ReplicaConflictReview> GetReplicaConflictReviewAsync(string replicaId, CancellationToken cancellationToken = default)
        {
            ReviewCalls++;
            return Task.FromResult(_resolved && ReviewAfterResolution is not null ? ReviewAfterResolution : ConflictReview);
        }

        public Task<SyncConflictResolutionResult> ResolveConflictsAsync(SyncConflictResolutionRequest request, CancellationToken cancellationToken = default)
        {
            ResolutionStarted = true;
            if (PendingResolution is not null) return PendingResolution;
            var result = Resolution ?? SyncConflictResolutionResult.Blocked(request.ConflictIds, request.Action, MissingBinding);
            if (result.BindingState is null)
            {
                _resolved = true;
            }

            return Task.FromResult(result);
        }
    }
}
