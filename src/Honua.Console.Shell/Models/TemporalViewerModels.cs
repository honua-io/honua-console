namespace Honua.Console.Shell.Models;

/// <summary>
/// Console-owned view models for the temporal data viewer and disconnected sync conflict review
/// surface (honua-console#43). These are not server wire contracts: the merged build binds the
/// server-owned temporal data history API from honua-server#1166 (as-of query, diff, attribution,
/// rollback) and the disconnected replica conflict review API from honua-server#1167 (named replica
/// metadata + conflict reads/resolution writes). Until those contracts land, the only registered
/// implementation surfaces an explicit missing-binding / unsupported-capability state — Console never
/// fabricates temporal history, checkpoints, diffs, rollback plans, or sync conflicts from a standing
/// mock (Console Patterns Charter section 11). The field set mirrors
/// <c>docs/architecture/temporal-data-viewer-information-model.md</c>.
/// </summary>
public sealed record TemporalViewerWorkspace(
    IReadOnlyList<TemporalSourceCapability> Sources,
    IReadOnlyList<TemporalCapabilityState> CapabilityStates);

/// <summary>
/// A neutral capability/binding state rendered above a temporal surface. Mirrors the Operate
/// capability-state vocabulary (<c>unknown</c>, <c>unsupported</c>, <c>missing</c>, <c>not configured</c>,
/// <c>forbidden</c>) so unsupported temporal sources render a capability explanation rather than an empty
/// viewer (issue AC #1), and an RBAC denial renders a forbidden explanation rather than fabricated data.
/// </summary>
public sealed record TemporalCapabilityState(
    string Surface,
    string State,
    string Contract,
    string Detail);

/// <summary>
/// Shared binding/permission state returned by every <see cref="Services.ITemporalCapabilityClient"/>
/// operation. A non-null value means the operation could not bind to a real server contract
/// (missing binding) or was refused (forbidden / unsupported) and the surface must render the
/// explanation instead of content. Mirrors <c>StudioWorkflowBindingState</c>.
/// </summary>
public sealed record TemporalBindingState(string Surface, string State, string Contract, string Detail)
{
    /// <summary>The server contract is not configured / has not landed; render a missing-binding explanation.</summary>
    public const string MissingBinding = "Missing binding";

    /// <summary>The caller lacks the temporal/history/rollback/conflict-review entitlement; render a forbidden explanation.</summary>
    public const string Forbidden = "Forbidden";

    /// <summary>The source declares it does not support the requested temporal/sync capability.</summary>
    public const string Unsupported = "Unsupported";

    public bool IsMissingBinding =>
        string.Equals(State, MissingBinding, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Server-owned declaration (honua-server#1166) of what temporal history a single layer or table can
/// support. The fields mirror <c>TemporalSourceCapability</c> in the information model. Capability
/// discovery is server-owned; Console only renders the declared mode and never promises rollback or
/// sync for a source the server did not mark eligible.
/// </summary>
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
    /// <summary>The server-owned history implementation backing the source (system_versioned, valid_time, …).</summary>
    public string? HistoryModel { get; init; }

    /// <summary>Whether geometry-level history/diff is supported for the source.</summary>
    public bool GeometryHistorySupported { get; init; }

    /// <summary>Whether attribute-level history/diff is supported for the source.</summary>
    public bool AttributeHistorySupported { get; init; }

    /// <summary>Whether the source persists named replicas + sync cursors (honua-server#1167).</summary>
    public bool ReplicaTrackingSupported { get; init; }

    /// <summary>
    /// Whether the caller holds the separately-permissioned history-read entitlement. History access is
    /// permissioned independently from current-data read (information model "Security And Governance").
    /// </summary>
    public bool HistoryReadPermitted { get; init; } = true;
}

/// <summary>
/// History mode a temporal source exposes, ordered from least to most capable. Mirrors the
/// information-model <c>mode</c> field.
/// </summary>
public enum TemporalMode
{
    /// <summary>No temporal history is available for the source.</summary>
    None,

    /// <summary>The source can be read at a point in time (as-of) but does not expose per-feature history.</summary>
    AsOf,

    /// <summary>The source exposes committed revision history per feature.</summary>
    History,

    /// <summary>The source can diff two states (added/removed/attribute/geometry changes).</summary>
    Diff,

    /// <summary>The source supports governed rollback into a new corrective operation.</summary>
    Rollback,
}

/// <summary>
/// Disconnected/offline sync direction a source supports, aligned with Esri sync concepts
/// (<c>syncCapabilities</c>) rather than a forked model. Mirrors the information-model
/// <c>sync_capability</c> field.
/// </summary>
public enum TemporalSyncCapability
{
    /// <summary>No disconnected sync workflow is supported.</summary>
    None,

    /// <summary>Replicas can download server changes but not upload edits.</summary>
    Download,

    /// <summary>Replicas can upload edits but not download server changes.</summary>
    Upload,

    /// <summary>Replicas can both upload and download (full disconnected editing).</summary>
    Bidirectional,
}

/// <summary>
/// Named or implicit history cursor for a source (information-model "Temporal Checkpoint"). Powers the
/// checkpoint selector and the now-vs-as-of comparison. Server-owned (honua-server#1166).
/// </summary>
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

/// <summary>Cursor kind a checkpoint represents (information-model <c>cursor_type</c>).</summary>
public enum TemporalCursorType
{
    Timestamp,
    Transaction,
    Release,
    Job,
    NamedCheckpoint,
}

/// <summary>
/// Result of listing checkpoints for a source. Carries the binding/permission state so the checkpoint
/// selector can render the missing-binding/forbidden explanation instead of an empty selector.
/// </summary>
public sealed record TemporalCheckpointList(
    IReadOnlyList<TemporalCheckpoint> Checkpoints,
    TemporalBindingState? BindingState = null);

/// <summary>
/// Server-generated comparison between two checkpoints / time cursors (information-model "Temporal Diff").
/// Powers the diff summary and the now-vs-as-of comparison. Server-owned (honua-server#1166); large diffs
/// are produced by the job runner and exposed as artifacts.
/// </summary>
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

/// <summary>Per-feature comparison row (information-model "Feature Diff").</summary>
public sealed record TemporalFeatureChange(
    string FeatureId,
    TemporalChangeType ChangeType,
    string? FromRevisionId,
    string? ToRevisionId,
    IReadOnlyList<TemporalAttributeChange> AttributeChanges,
    bool GeometryChanged,
    string? ActorId,
    string? OperationRef);

/// <summary>Field-level before/after change. Sensitive fields can be masked by the server.</summary>
public sealed record TemporalAttributeChange(string Field, string? Before, string? After, bool Masked);

/// <summary>Per-feature change classification (information-model <c>change_type</c>).</summary>
public enum TemporalChangeType
{
    Added,
    Removed,
    Updated,
    Unchanged,
}

/// <summary>
/// Complete revision history for one feature (information-model "Feature Timeline" / "Temporal Revision").
/// Carries the binding state so the timeline renders the explanation when unbound.
/// </summary>
public sealed record TemporalFeatureTimeline(
    string FeatureId,
    string SourceId,
    IReadOnlyList<TemporalRevision> Revisions,
    TemporalBindingState? BindingState = null);

/// <summary>One committed state of one feature (information-model "Temporal Revision").</summary>
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

/// <summary>Revision operation kind (information-model <c>operation</c>).</summary>
public enum TemporalRevisionOperation
{
    Insert,
    Update,
    Delete,
    Restore,
}

/// <summary>
/// Server-authored, immutable rollback plan (information-model "Temporal Rollback Plan"). Console
/// renders this as evidence; it does not synthesize the plan. AC #2: executing a plan creates a governed
/// job/checkpoint rather than erasing history.
/// </summary>
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

/// <summary>Rollback target scope (information-model <c>target_scope</c>).</summary>
public enum TemporalRollbackScope
{
    Feature,
    Selection,
    Layer,
    ChangeSet,
    Release,
}

/// <summary>How a rollback is applied (information-model <c>rollback_mode</c>).</summary>
public enum TemporalRollbackMode
{
    MetadataOnly,
    DataRevert,
    ServiceRevisionSwitch,
    ScriptRequired,
    Manual,
}

/// <summary>
/// Tracked execution record for an approved rollback (information-model "Temporal Rollback Operation").
/// AC #2: produced by executing an immutable plan; it creates a new job + a new result checkpoint and
/// references audit events rather than erasing history.
/// </summary>
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

/// <summary>
/// Durable server-side record for a disconnected/offline replica (information-model "Disconnected
/// Replica"). Esri-aligned: replica name, owner, device, base checkpoint, and sync generation. Server-owned
/// (honua-server#1167).
/// </summary>
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

/// <summary>Replica lifecycle status (information-model <c>status</c>).</summary>
public enum ReplicaStatus
{
    Active,
    Closed,
    Expired,
    Conflicted,
    Unregistered,
}

/// <summary>
/// The replica conflict queue: replicas with sync state plus the binding/permission state so the queue
/// renders the explanation instead of an empty list when unbound or forbidden. Server-owned
/// (honua-server#1167).
/// </summary>
public sealed record ReplicaConflictQueue(
    IReadOnlyList<DisconnectedReplica> Replicas,
    TemporalBindingState? BindingState = null);

/// <summary>
/// Server-detected conflict between a disconnected edit and current server state (information-model
/// "Sync Conflict"). Carries base/client/server revisions and field/geometry conflict markers.
/// </summary>
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

/// <summary>Conflict classification (information-model <c>conflict_type</c>).</summary>
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

/// <summary>One field-level conflict: base/client/server values for base-vs-client-vs-server comparison.</summary>
public sealed record SyncFieldConflict(string Field, string? BaseValue, string? ClientValue, string? ServerValue);

/// <summary>Conflict lifecycle status (information-model <c>status</c>).</summary>
public enum SyncConflictStatus
{
    Pending,
    Resolved,
    Rejected,
    Superseded,
}

/// <summary>
/// The conflict review batch for a single replica: the replica, its conflicts, server-side change-set
/// context, and the binding/permission state. Server-owned (honua-server#1167).
/// </summary>
public sealed record ReplicaConflictReview(
    DisconnectedReplica? Replica,
    IReadOnlyList<SyncConflict> Conflicts,
    TemporalBindingState? BindingState = null);

/// <summary>Resolution choice for one or a batch of conflicts (information-model <c>resolution_action</c>).</summary>
public enum SyncResolutionAction
{
    AcceptClient,
    KeepServer,
    MergeFields,
    RestoreBase,
    RejectClientEdit,
    Defer,
}

/// <summary>
/// A requested resolution decision for one or a batch of conflicts. Console builds this; the server
/// applies it and returns committed evidence. Field-level merge maps a field to the chosen source.
/// </summary>
public sealed record SyncConflictResolutionRequest(
    IReadOnlyList<string> ConflictIds,
    SyncResolutionAction Action,
    IReadOnlyDictionary<string, string>? FieldResolutions = null);

/// <summary>
/// Result of writing a conflict resolution (information-model "Sync Conflict Resolution"). AC #3:
/// resolution writes audit evidence and a committed change set. Carries the binding/permission state so a
/// blocked write renders the explanation instead of claiming success.
/// </summary>
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
    /// <summary>AC #3: a committed resolution must carry a change set and at least one audit event.</summary>
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
