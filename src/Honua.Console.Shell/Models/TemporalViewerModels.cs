namespace Honua.Console.Shell.Models;

public sealed record TemporalViewerWorkspace(
    IReadOnlyList<TemporalSourceCapability> Sources,
    IReadOnlyList<TemporalCapabilityState> CapabilityStates);

public sealed record TemporalCapabilityState(
    string Surface,
    string State,
    string Contract,
    string Detail);

// When set, the surface explains this state instead of showing content.
public sealed record TemporalBindingState(string Surface, string State, string Contract, string Detail)
{
    public const string MissingBinding = "Missing binding";

    public const string Forbidden = "Forbidden";

    public const string Unsupported = "Unsupported";

    public bool IsMissingBinding =>
        string.Equals(State, MissingBinding, StringComparison.OrdinalIgnoreCase);
}

public sealed record TemporalSourceCapability(
    string SourceId,
    string ResourceId,
    string LayerId,
    TemporalMode Mode,
    TemporalSyncCapability SyncCapability,
    bool RollbackSupported,
    bool SyncConflictReviewSupported,
    string? RetentionPolicyId)
{
    public string? HistoryModel { get; init; }

    public bool GeometryHistorySupported { get; init; }

    public bool AttributeHistorySupported { get; init; }

    public bool ReplicaTrackingSupported { get; init; }

    // History read is a separate entitlement from current-data read.
    public bool HistoryReadPermitted { get; init; } = true;
}

public enum TemporalMode
{
    None,

    AsOf,

    History,

    Diff,

    Rollback,
}

public enum TemporalSyncCapability
{
    None,

    Download,

    Upload,

    Bidirectional,
}

public sealed record TemporalCheckpoint(
    string CheckpointId,
    string SourceId,
    TemporalCursorType CursorType,
    string CursorValue,
    string Label,
    DateTimeOffset CreatedAt,
    string? CreatedBy,
    string? OperationRef,
    string? JobRunId,
    string? MetadataReleaseId);

public enum TemporalCursorType
{
    Timestamp,
    Transaction,
    Release,
    Job,
    NamedCheckpoint,
}

public sealed record TemporalCheckpointList(
    IReadOnlyList<TemporalCheckpoint> Checkpoints,
    TemporalBindingState? BindingState = null);

public sealed record TemporalDiff(
    string DiffId,
    string SourceId,
    string FromCheckpointId,
    string ToCheckpointId,
    int AddedFeatures,
    int RemovedFeatures,
    int UpdatedFeatures,
    int GeometryChangedFeatures,
    int AttributeChangedFeatures,
    IReadOnlyList<TemporalFeatureChange> SampleFeatureChanges,
    DateTimeOffset GeneratedAt,
    TemporalBindingState? BindingState = null)
{
    public static TemporalDiff Blocked(TemporalBindingState state) =>
        new(string.Empty, string.Empty, string.Empty, string.Empty, 0, 0, 0, 0, 0, [], default, state);
}

public sealed record TemporalFeatureChange(
    string FeatureId,
    TemporalChangeType ChangeType,
    string? FromRevisionId,
    string? ToRevisionId,
    IReadOnlyList<TemporalAttributeChange> AttributeChanges,
    bool GeometryChanged,
    string? ActorId,
    string? OperationRef);

public sealed record TemporalAttributeChange(string Field, string? Before, string? After, bool Masked);

public enum TemporalChangeType
{
    Added,
    Removed,
    Updated,
    Unchanged,
}

public sealed record TemporalFeatureTimeline(
    string FeatureId,
    string SourceId,
    IReadOnlyList<TemporalRevision> Revisions,
    TemporalBindingState? BindingState = null);

public sealed record TemporalRevision(
    string RevisionId,
    DateTimeOffset TransactionTime,
    TemporalRevisionOperation Operation,
    string? ActorDisplayName,
    string? SourceClient,
    string? JobRunId,
    string? ReleaseOperationId,
    string? Reason,
    bool GeometryChanged,
    int ChangedFieldCount,
    bool RestoreAllowed);

public enum TemporalRevisionOperation
{
    Insert,
    Update,
    Delete,
    Restore,
}

// Executing this plan creates a governed job and a new checkpoint; it does not erase history.
public sealed record TemporalRollbackPlan(
    string RollbackPlanId,
    string SourceId,
    TemporalRollbackScope TargetScope,
    string TargetCheckpointId,
    string CurrentCheckpointId,
    int AffectedFeatureCount,
    bool RequiresJob,
    bool RequiresApproval,
    TemporalRollbackMode RollbackMode,
    string RiskLevel,
    IReadOnlyList<string> ValidationFindings,
    IReadOnlyList<string> CompatibilityFindings,
    TemporalBindingState? BindingState = null)
{
    public static TemporalRollbackPlan Blocked(TemporalBindingState state) =>
        new(string.Empty, string.Empty, TemporalRollbackScope.Feature, string.Empty, string.Empty, 0,
            RequiresJob: false, RequiresApproval: false, TemporalRollbackMode.Manual, "unknown", [], [], state);
}

public enum TemporalRollbackScope
{
    Feature,
    Selection,
    Layer,
    ChangeSet,
    Release,
}

public enum TemporalRollbackMode
{
    MetadataOnly,
    DataRevert,
    ServiceRevisionSwitch,
    ScriptRequired,
    Manual,
}

public sealed record TemporalRollbackOperation(
    string RollbackOperationId,
    string RollbackPlanId,
    string? JobRunId,
    string Status,
    string? RequestedBy,
    string? ApprovedBy,
    string? ResultCheckpointId,
    IReadOnlyList<string> AuditEventIds,
    string? FailureReason,
    TemporalBindingState? BindingState = null)
{
    public static TemporalRollbackOperation Blocked(TemporalBindingState state) =>
        new(string.Empty, string.Empty, JobRunId: null, "blocked", RequestedBy: null, ApprovedBy: null,
            ResultCheckpointId: null, [], FailureReason: null, state);
}

public sealed record DisconnectedReplica(
    string ReplicaId,
    string ReplicaName,
    string SourceId,
    string OwnerId,
    string? DeviceId,
    TemporalSyncCapability SyncDirection,
    string BaseCheckpointId,
    long ReplicaServerGen,
    DateTimeOffset? LastSyncAt,
    ReplicaStatus Status,
    int PendingConflictCount);

public enum ReplicaStatus
{
    Active,
    Closed,
    Expired,
    Conflicted,
    Unregistered,
}

public sealed record ReplicaConflictQueue(
    IReadOnlyList<DisconnectedReplica> Replicas,
    TemporalBindingState? BindingState = null);

public sealed record SyncConflict(
    string ConflictId,
    string ReplicaId,
    string SourceId,
    string LayerId,
    string FeatureId,
    SyncConflictType ConflictType,
    string? BaseRevisionId,
    string? ServerRevisionId,
    IReadOnlyList<SyncFieldConflict> FieldConflicts,
    bool GeometryConflict,
    DateTimeOffset DetectedAt,
    SyncConflictStatus Status);

public enum SyncConflictType
{
    Attribute,
    Geometry,
    DeleteUpdate,
    UpdateDelete,
    InsertDuplicate,
    Attachment,
    Relationship,
}

public sealed record SyncFieldConflict(string Field, string? BaseValue, string? ClientValue, string? ServerValue);

public enum SyncConflictStatus
{
    Pending,
    Resolved,
    Rejected,
    Superseded,
}

public sealed record ReplicaConflictReview(
    DisconnectedReplica? Replica,
    IReadOnlyList<SyncConflict> Conflicts,
    TemporalBindingState? BindingState = null);

public enum SyncResolutionAction
{
    AcceptClient,
    KeepServer,
    MergeFields,
    RestoreBase,
    RejectClientEdit,
    Defer,
}

public sealed record SyncConflictResolutionRequest(
    IReadOnlyList<string> ConflictIds,
    SyncResolutionAction Action,
    IReadOnlyDictionary<string, string>? FieldResolutions = null);

public sealed record SyncConflictResolutionResult(
    string? ResolutionId,
    IReadOnlyList<string> ConflictIds,
    SyncResolutionAction Action,
    string? ResultRevisionId,
    string? ResultChangeSetId,
    IReadOnlyList<string> AuditEventIds,
    string? JobRunId,
    TemporalBindingState? BindingState = null)
{
    public bool WroteAuditedChangeSet =>
        BindingState is null
        && !string.IsNullOrWhiteSpace(ResultChangeSetId)
        && AuditEventIds.Count > 0;

    public static SyncConflictResolutionResult Blocked(
        IReadOnlyList<string> conflictIds,
        SyncResolutionAction action,
        TemporalBindingState state) =>
        new(ResolutionId: null, conflictIds, action, ResultRevisionId: null, ResultChangeSetId: null, [],
            JobRunId: null, state);
}
