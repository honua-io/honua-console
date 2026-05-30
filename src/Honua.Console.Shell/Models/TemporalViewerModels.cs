namespace Honua.Console.Shell.Models;

/// <summary>
/// Console-owned view models for the temporal data viewer and disconnected sync conflict review
/// surface (honua-console#43). These are not server wire contracts: the merged build binds the
/// server-owned temporal capability manifest from honua-server#1166 and the disconnected sync
/// conflict review contract from honua-server#1167. Until those contracts land, the only registered
/// implementation surfaces an explicit missing-binding / unsupported-capability state — Console never
/// fabricates temporal history, checkpoints, or sync conflicts from a standing mock (Console Patterns
/// Charter section 11).
/// </summary>
public sealed record TemporalViewerWorkspace(
    IReadOnlyList<TemporalSourceCapability> Sources,
    IReadOnlyList<TemporalCapabilityState> CapabilityStates);

/// <summary>
/// A neutral capability/binding state rendered above the viewer. Mirrors the Operate capability-state
/// vocabulary (<c>unknown</c>, <c>unsupported</c>, <c>missing</c>, <c>not configured</c>) so unsupported
/// temporal sources render a capability explanation rather than an empty viewer (issue AC #1).
/// </summary>
public sealed record TemporalCapabilityState(
    string Surface,
    string State,
    string Contract,
    string Detail);

/// <summary>
/// Server-owned declaration (honua-server#1166) of what temporal history a single layer or table can
/// support. The fields mirror <c>TemporalSourceCapability</c> in
/// <c>docs/architecture/temporal-data-viewer-information-model.md</c>. Capability discovery is
/// server-owned; Console only renders the declared mode and never promises rollback or sync for a
/// source the server did not mark eligible.
/// </summary>
public sealed record TemporalSourceCapability(
    string SourceId,
    string ResourceId,
    string LayerId,
    TemporalMode Mode,
    TemporalSyncCapability SyncCapability,
    bool RollbackSupported,
    bool SyncConflictReviewSupported,
    string? RetentionPolicyId);

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
