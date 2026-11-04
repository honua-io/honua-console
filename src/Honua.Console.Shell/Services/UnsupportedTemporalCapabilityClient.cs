using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public sealed class UnsupportedTemporalCapabilityClient : ITemporalCapabilityClient
{
    internal const string Surface = "Temporal viewer";
    internal const string Contract = "honua-server#1166 / honua-server#1167";

    internal const string Detail =
        "Temporal history and disconnected sync conflict review bind to the server-owned temporal data "
        + "history API (honua-server#1166) and the disconnected replica conflict review API "
        + "(honua-server#1167). Configure Honua:Server:BaseUrl (or HONUA_SERVER_BASE_URL) and wait for "
        + "those contracts to land; Console will not fabricate temporal data from a mock.";

    private static readonly TemporalCapabilityState MissingCapabilityState =
        new(Surface, TemporalBindingState.MissingBinding, Contract, Detail);

    private static readonly TemporalBindingState MissingBinding =
        new(Surface, TemporalBindingState.MissingBinding, Contract, Detail);

    private static readonly TemporalViewerWorkspace Workspace =
        new(Sources: [], CapabilityStates: [MissingCapabilityState]);

    public Task<TemporalViewerWorkspace> GetWorkspaceAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Workspace);

    public Task<TemporalCheckpointList> GetCheckpointsAsync(
        string sourceId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new TemporalCheckpointList([], MissingBinding));

    public Task<TemporalDiff> GetDiffAsync(
        string sourceId,
        string fromCheckpointId,
        string toCheckpointId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(TemporalDiff.Blocked(MissingBinding));

    public Task<TemporalFeatureTimeline> GetFeatureTimelineAsync(
        string sourceId,
        string featureId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new TemporalFeatureTimeline(featureId, sourceId, [], MissingBinding));

    public Task<TemporalRollbackPlan> CreateRollbackPlanAsync(
        string sourceId,
        TemporalRollbackScope scope,
        string targetCheckpointId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(TemporalRollbackPlan.Blocked(MissingBinding));

    public Task<TemporalRollbackOperation> ExecuteRollbackAsync(
        string rollbackPlanId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(TemporalRollbackOperation.Blocked(MissingBinding));

    public Task<ReplicaConflictQueue> GetReplicaConflictQueueAsync(
        string sourceId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new ReplicaConflictQueue([], MissingBinding));

    public Task<ReplicaConflictReview> GetReplicaConflictReviewAsync(
        string replicaId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new ReplicaConflictReview(Replica: null, [], MissingBinding));

    public Task<SyncConflictResolutionResult> ResolveConflictsAsync(
        SyncConflictResolutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Task.FromResult(
            SyncConflictResolutionResult.Blocked(request.ConflictIds, request.Action, MissingBinding));
    }
}
