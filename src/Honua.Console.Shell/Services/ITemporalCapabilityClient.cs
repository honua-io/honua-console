using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public interface ITemporalCapabilityClient
{
    Task<TemporalViewerWorkspace> GetWorkspaceAsync(CancellationToken cancellationToken = default);

    Task<TemporalCheckpointList> GetCheckpointsAsync(string sourceId, CancellationToken cancellationToken = default);

    Task<TemporalDiff> GetDiffAsync(
        string sourceId,
        string fromCheckpointId,
        string toCheckpointId,
        CancellationToken cancellationToken = default);

    Task<TemporalFeatureTimeline> GetFeatureTimelineAsync(
        string sourceId,
        string featureId,
        CancellationToken cancellationToken = default);

    // Planning returns a plan and does not change feature state.
    Task<TemporalRollbackPlan> CreateRollbackPlanAsync(
        string sourceId,
        TemporalRollbackScope scope,
        string targetCheckpointId,
        CancellationToken cancellationToken = default);

    // Execute submits a governed corrective job and leaves earlier checkpoints in place. A new checkpoint is recorded only when corrective edits advance the generation.
    Task<TemporalRollbackOperation> ExecuteRollbackAsync(
        string rollbackPlanId,
        CancellationToken cancellationToken = default);

    Task<ReplicaConflictQueue> GetReplicaConflictQueueAsync(string sourceId, CancellationToken cancellationToken = default);

    Task<ReplicaConflictReview> GetReplicaConflictReviewAsync(string replicaId, CancellationToken cancellationToken = default);

    Task<SyncConflictResolutionResult> ResolveConflictsAsync(
        SyncConflictResolutionRequest request,
        CancellationToken cancellationToken = default);
}
