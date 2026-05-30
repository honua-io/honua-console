using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Temporal data viewer + disconnected sync conflict review data source (honua-console#43).
/// The merged build binds the server-owned temporal data history API (honua-server#1166: as-of query,
/// diff, attribution, rollback) and the disconnected replica conflict review API (honua-server#1167:
/// named replica metadata + conflict reads/resolution writes); there is no standing in-memory temporal
/// client in the shipped result (Console Patterns Charter section 11). When no server binding is
/// configured — or those contracts have not landed — the unsupported implementation surfaces an explicit
/// missing-binding state from every operation instead of fabricating temporal history, diffs, rollback
/// plans, or sync conflicts. Each result type carries a <see cref="TemporalBindingState"/> so the caller
/// can render the binding/forbidden/unsupported explanation in place of content.
/// </summary>
public interface ITemporalCapabilityClient
{
    /// <summary>
    /// Lists the temporal-eligible sources the caller may inspect plus any binding/capability states.
    /// Unsupported or unbound sources are reported through <see cref="TemporalViewerWorkspace.CapabilityStates"/>
    /// so the viewer can render a capability explanation rather than an empty surface (issue AC #1).
    /// </summary>
    Task<TemporalViewerWorkspace> GetWorkspaceAsync(CancellationToken cancellationToken = default);

    /// <summary>Lists named/implicit checkpoints for a source, powering the checkpoint selector and time scrubber.</summary>
    Task<TemporalCheckpointList> GetCheckpointsAsync(string sourceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Computes the server-generated diff between two checkpoints for the now-vs-as-of comparison and diff
    /// summary. Large diffs are produced by the server job runner and exposed as artifacts (honua-server#1166).
    /// </summary>
    Task<TemporalDiff> GetDiffAsync(
        string sourceId,
        string fromCheckpointId,
        string toCheckpointId,
        CancellationToken cancellationToken = default);

    /// <summary>Loads the complete revision history for one feature (feature timeline entry point).</summary>
    Task<TemporalFeatureTimeline> GetFeatureTimelineAsync(
        string sourceId,
        string featureId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Requests a server-authored, immutable rollback plan for the given scope/checkpoint. The plan is
    /// evidence only; nothing is mutated until <see cref="ExecuteRollbackAsync"/> runs it (issue AC #2).
    /// </summary>
    Task<TemporalRollbackPlan> CreateRollbackPlanAsync(
        string sourceId,
        TemporalRollbackScope scope,
        string targetCheckpointId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes an approved rollback plan. AC #2: this creates a governed job + a new result checkpoint and
    /// references audit events; it never erases immutable history.
    /// </summary>
    Task<TemporalRollbackOperation> ExecuteRollbackAsync(
        string rollbackPlanId,
        CancellationToken cancellationToken = default);

    /// <summary>Lists disconnected replicas for a source with owner/device/base-checkpoint/sync-gen/status (replica conflict queue).</summary>
    Task<ReplicaConflictQueue> GetReplicaConflictQueueAsync(string sourceId, CancellationToken cancellationToken = default);

    /// <summary>Loads the conflict review batch for one replica (base/client/server comparison + field/geometry markers).</summary>
    Task<ReplicaConflictReview> GetReplicaConflictReviewAsync(string replicaId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes a resolution decision for one conflict or a batch. AC #3: the server records audit evidence and
    /// a committed change set, returned on the result; a blocked write carries a binding state instead.
    /// </summary>
    Task<SyncConflictResolutionResult> ResolveConflictsAsync(
        SyncConflictResolutionRequest request,
        CancellationToken cancellationToken = default);
}
