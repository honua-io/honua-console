using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// The console's branch-version management OPERATIONS for the Operate version-manager and conflict-resolution
/// surfaces (/operate/versions, honua-console#177). Drives REAL calls against honua-server's GeoServices
/// VersionManagementServer (#371 / PR #1551): list/create/alter/delete versions, reconcile with an
/// auto-resolution policy, inspect the pending 3-way conflict set, submit manual per-feature resolutions, and
/// post to DEFAULT. The live implementation is DI-gated on a configured server base URL; when no server is
/// configured the surface binds to <see cref="UnsupportedVersionManagementOperation"/>, which returns
/// missing-binding results and performs no network call (Console Patterns Charter section 11 — never
/// fabricate a version operation).
/// </summary>
public interface IVersionManagementOperation
{
    /// <summary>Lists the branch versions for a service.</summary>
    Task<OperateVersionListView> ListVersionsAsync(
        string serviceId,
        CancellationToken cancellationToken = default);

    /// <summary>Creates a branch version.</summary>
    Task<VersionOperationResult> CreateVersionAsync(
        CreateVersionCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>Alters a branch version's name/access/description.</summary>
    Task<VersionOperationResult> AlterVersionAsync(
        AlterVersionCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes a branch version.</summary>
    Task<VersionOperationResult> DeleteVersionAsync(
        string serviceId,
        string versionGuid,
        CancellationToken cancellationToken = default);

    /// <summary>Reconciles a branch version against DEFAULT with an auto-resolution policy.</summary>
    Task<ReconcileResultView> ReconcileAsync(
        ReconcileVersionCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>Reads the pending 3-way conflict set for a branch version.</summary>
    Task<VersionConflictsView> InspectConflictsAsync(
        string serviceId,
        string versionGuid,
        CancellationToken cancellationToken = default);

    /// <summary>Submits manual per-feature resolution choices and returns the resolved/remaining counts.</summary>
    Task<ResolveConflictsResultView> ResolveConflictsAsync(
        string serviceId,
        string versionGuid,
        IReadOnlyList<ConflictResolutionChoice> resolutions,
        CancellationToken cancellationToken = default);

    /// <summary>Posts a reconciled branch version's changes onto DEFAULT (blocked while conflicts remain).</summary>
    Task<VersionOperationResult> PostAsync(
        string serviceId,
        string versionGuid,
        CancellationToken cancellationToken = default);
}

/// <summary>A single operator resolution choice for a conflicting feature.</summary>
public sealed record ConflictResolutionChoice(int LayerId, long ObjectId, string Choice);

/// <summary>Outcome of a resolveConflicts call: the operation result plus the resolved/remaining counts.</summary>
public sealed record ResolveConflictsResultView
{
    public required VersionOperationResult Operation { get; init; }

    public int Resolved { get; init; }

    public int Remaining { get; init; }

    public bool CanPost { get; init; }
}
