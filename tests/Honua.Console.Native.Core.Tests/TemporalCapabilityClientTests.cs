using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.Native.Core.Tests;

/// <summary>
/// Host-independent unit coverage for the merged-build temporal client
/// (<see cref="UnsupportedTemporalCapabilityClient"/>) and the temporal result-model invariants
/// (honua-console#43). Asserts that every operation reports the honua-server#1166/#1167 missing-binding
/// state and returns no fabricated temporal/sync content (Console Patterns Charter section 11), and that
/// the result envelopes encode the acceptance-criteria invariants (governed rollback evidence, audited
/// resolution change set).
/// </summary>
public sealed class TemporalCapabilityClientTests
{
    private readonly UnsupportedTemporalCapabilityClient _client = new();

    [Fact]
    public async Task GetWorkspace_ReportsMissingBinding_WithoutSources()
    {
        var workspace = await _client.GetWorkspaceAsync();

        Assert.Empty(workspace.Sources);
        var state = Assert.Single(workspace.CapabilityStates);
        Assert.Equal(TemporalBindingState.MissingBinding, state.State);
        Assert.Contains("honua-server#1166", state.Contract, StringComparison.Ordinal);
        Assert.Contains("honua-server#1167", state.Contract, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetCheckpoints_IsMissingBinding_AndEmpty()
    {
        var result = await _client.GetCheckpointsAsync("src-parcels");

        Assert.Empty(result.Checkpoints);
        AssertMissingBinding(result.BindingState);
    }

    [Fact]
    public async Task GetDiff_IsMissingBinding_AndCarriesNoCounts()
    {
        var diff = await _client.GetDiffAsync("src-parcels", "cp-1", "cp-2");

        AssertMissingBinding(diff.BindingState);
        Assert.Equal(0, diff.AddedFeatures);
        Assert.Equal(0, diff.RemovedFeatures);
        Assert.Empty(diff.SampleFeatureChanges);
    }

    [Fact]
    public async Task GetFeatureTimeline_IsMissingBinding_WithNoRevisions()
    {
        var timeline = await _client.GetFeatureTimelineAsync("src-parcels", "feature-9");

        Assert.Equal("feature-9", timeline.FeatureId);
        Assert.Empty(timeline.Revisions);
        AssertMissingBinding(timeline.BindingState);
    }

    [Fact]
    public async Task CreateRollbackPlan_IsMissingBinding_AndPlansNothing()
    {
        var plan = await _client.CreateRollbackPlanAsync("src-parcels", TemporalRollbackScope.ChangeSet, "cp-1");

        AssertMissingBinding(plan.BindingState);
        Assert.Equal(0, plan.AffectedFeatureCount);
    }

    [Fact]
    public async Task ExecuteRollback_IsMissingBinding_AndCreatesNoCheckpoint()
    {
        var operation = await _client.ExecuteRollbackAsync("plan-1");

        AssertMissingBinding(operation.BindingState);
        // AC #2: a blocked execution never produces a result checkpoint or audit evidence.
        Assert.Null(operation.ResultCheckpointId);
        Assert.Empty(operation.AuditEventIds);
    }

    [Fact]
    public async Task GetReplicaConflictQueue_IsMissingBinding_AndEmpty()
    {
        var queue = await _client.GetReplicaConflictQueueAsync("src-parcels");

        Assert.Empty(queue.Replicas);
        AssertMissingBinding(queue.BindingState);
    }

    [Fact]
    public async Task GetReplicaConflictReview_IsMissingBinding_WithNoReplica()
    {
        var review = await _client.GetReplicaConflictReviewAsync("replica-1");

        Assert.Null(review.Replica);
        Assert.Empty(review.Conflicts);
        AssertMissingBinding(review.BindingState);
    }

    [Fact]
    public async Task ResolveConflicts_IsMissingBinding_AndWritesNoAuditEvidence()
    {
        var request = new SyncConflictResolutionRequest(["conflict-1", "conflict-2"], SyncResolutionAction.KeepServer);

        var result = await _client.ResolveConflictsAsync(request);

        AssertMissingBinding(result.BindingState);
        // AC #3: a blocked resolution writes no change set / audit events and is not "wrote audited change set".
        Assert.False(result.WroteAuditedChangeSet);
        Assert.Null(result.ResultChangeSetId);
        Assert.Empty(result.AuditEventIds);
        Assert.Equal(SyncResolutionAction.KeepServer, result.Action);
        Assert.Equal(["conflict-1", "conflict-2"], result.ConflictIds);
    }

    [Fact]
    public async Task ResolveConflicts_NullRequest_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.ResolveConflictsAsync(null!));
    }

    [Fact]
    public void ResolutionResult_WroteAuditedChangeSet_RequiresChangeSetAndAuditEvents()
    {
        // AC #3 invariant: only a committed resolution with both a change set and audit events counts.
        var committed = new SyncConflictResolutionResult(
            "res-1", ["c-1"], SyncResolutionAction.MergeFields,
            ResultRevisionId: "rev-9", ResultChangeSetId: "cs-3",
            AuditEventIds: ["audit-1"], JobRunId: "job-7");
        Assert.True(committed.WroteAuditedChangeSet);

        var noChangeSet = committed with { ResultChangeSetId = null };
        Assert.False(noChangeSet.WroteAuditedChangeSet);

        var noAudit = committed with { AuditEventIds = [] };
        Assert.False(noAudit.WroteAuditedChangeSet);

        var blocked = committed with { BindingState = new TemporalBindingState("s", TemporalBindingState.MissingBinding, "c", "d") };
        Assert.False(blocked.WroteAuditedChangeSet);
    }

    [Fact]
    public void BindingState_IsMissingBinding_OnlyForMissingBindingState()
    {
        Assert.True(new TemporalBindingState("s", TemporalBindingState.MissingBinding, "c", "d").IsMissingBinding);
        Assert.False(new TemporalBindingState("s", TemporalBindingState.Forbidden, "c", "d").IsMissingBinding);
        Assert.False(new TemporalBindingState("s", TemporalBindingState.Unsupported, "c", "d").IsMissingBinding);
    }

    private static void AssertMissingBinding(TemporalBindingState? state)
    {
        Assert.NotNull(state);
        Assert.Equal(TemporalBindingState.MissingBinding, state!.State);
        Assert.True(state.IsMissingBinding);
        Assert.Contains("honua-server#1166", state.Contract, StringComparison.Ordinal);
        Assert.Contains("honua-server#1167", state.Contract, StringComparison.Ordinal);
    }
}
