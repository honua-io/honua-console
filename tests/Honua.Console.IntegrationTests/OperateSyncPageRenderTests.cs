using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free render coverage for the disconnected-sync conflict review surface (/operate/sync, UI-046;
/// honua-server#1167/#1287). Asserts the merged-build missing-binding capability state (no fabricated
/// replicas/conflicts) plus the bound queue, base/client/server comparison, and committed change-set
/// evidence. Drives the page through the sync slice of ITemporalCapabilityClient (a fake, never a mock
/// server). The merged-build UnsupportedTemporalCapabilityClient is exercised through real DI.
/// </summary>
public sealed class OperateSyncPageRenderTests
{
    [Fact]
    public void Sync_WhenBindingMissing_RendersNotBoundSurfaceNotEmptyQueue()
    {
        var page = Render(new FakeSyncClient
        {
            Workspace = new TemporalViewerWorkspace([], [MissingBindingState])
        });

        page.WaitForAssertion(
            () => Assert.Contains("Sync conflict review is not bound", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Contains("honua-server#1167", page.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("<table", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Sync_MergedBuildPage_RendersMissingBindingThroughRealDi()
    {
        using var ctx = new Bunit.BunitContext();
        ctx.Services.AddSingleton<ITemporalCapabilityClient, UnsupportedTemporalCapabilityClient>();

        var page = ctx.Render<OperateSyncPage>();

        page.WaitForAssertion(
            () => Assert.Contains("Sync conflict review is not bound", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.DoesNotContain("<table", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Sync_WhenSyncSourceBound_ListsReplicaQueueAndResolvesConflict()
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
        var page = Render(client);

        page.WaitForAssertion(() => Assert.Contains("Open queue", page.Markup, StringComparison.Ordinal), TimeSpan.FromSeconds(5));
        ClickByText(page, "Open queue");
        page.WaitForAssertion(() => Assert.Contains("Field Crew 7", page.Markup, StringComparison.Ordinal), TimeSpan.FromSeconds(5));
        Assert.Contains("Replica Conflict Queue", page.Markup, StringComparison.Ordinal);

        ClickByText(page, "Review");
        page.WaitForAssertion(() => Assert.Contains("Base / Client / Server Comparison", page.Markup, StringComparison.Ordinal), TimeSpan.FromSeconds(5));
        Assert.Contains("owner_name", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Geometry conflict", page.Markup, StringComparison.Ordinal);

        ClickByText(page, "Client wins");
        page.WaitForAssertion(() => Assert.Contains("Committed Change Set", page.Markup, StringComparison.Ordinal), TimeSpan.FromSeconds(5));
        Assert.Contains("cs-9", page.Markup, StringComparison.Ordinal);
        Assert.Contains("audit-1", page.Markup, StringComparison.Ordinal);
    }

    private static IRenderedComponent<OperateSyncPage> Render(ITemporalCapabilityClient client)
    {
        var ctx = new Bunit.BunitContext();
        ctx.Services.AddSingleton(client);
        return ctx.Render<OperateSyncPage>();
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
            Task.FromResult(ReplicaQueue);

        public Task<ReplicaConflictReview> GetReplicaConflictReviewAsync(string replicaId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_resolved && ReviewAfterResolution is not null ? ReviewAfterResolution : ConflictReview);

        public Task<SyncConflictResolutionResult> ResolveConflictsAsync(SyncConflictResolutionRequest request, CancellationToken cancellationToken = default)
        {
            var result = Resolution ?? SyncConflictResolutionResult.Blocked(request.ConflictIds, request.Action, MissingBinding);
            if (result.BindingState is null)
            {
                _resolved = true;
            }

            return Task.FromResult(result);
        }
    }
}
