using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Live implementation of the branch-version management operations. Drives honua-server's GeoServices
/// VersionManagementServer through <see cref="IHonuaVersionManagementClient"/> and maps each server result
/// (or rejection) into the Operate view/result models. It never fabricates success — every result reflects
/// what the server returned (honua-console#177, honua-server#371 / PR #1551).
/// </summary>
public sealed class HonuaServerVersionManagementOperation : IVersionManagementOperation
{
    private readonly IHonuaVersionManagementClient _client;

    /// <summary>Creates the operation over the VersionManagementServer client.</summary>
    public HonuaServerVersionManagementOperation(IHonuaVersionManagementClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public async Task<OperateVersionListView> ListVersionsAsync(
        string serviceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceId);

        var result = await _client.ListVersionsAsync(serviceId, cancellationToken).ConfigureAwait(false);
        if (result.Data is { } versions)
        {
            return new OperateVersionListView
            {
                Bound = true,
                Versions = versions.Select(OperateVersionView.FromContract).ToArray()
            };
        }

        var issue = result.Issue;
        return OperateVersionListView.Unbound(
            issue?.State ?? "Unavailable",
            issue?.Detail ?? "The Honua server did not return branch versions for this service.",
            issue?.Contract);
    }

    public async Task<VersionOperationResult> CreateVersionAsync(
        CreateVersionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var result = await _client
            .CreateVersionAsync(
                command.ServiceId,
                command.VersionName,
                command.Owner,
                ParseAccess(command.Access),
                command.Description,
                cancellationToken)
            .ConfigureAwait(false);

        if (result.Data is { } info && !string.IsNullOrWhiteSpace(info.VersionName))
        {
            return VersionOperationResult.Success(
                "Created",
                $"Created branch version '{info.VersionName}' on honua-server.");
        }

        return Failure(result.Issue, "The Honua server did not accept the create-version request.");
    }

    public async Task<VersionOperationResult> AlterVersionAsync(
        AlterVersionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var result = await _client
            .AlterVersionAsync(
                command.ServiceId,
                command.VersionGuid,
                command.VersionName,
                string.IsNullOrWhiteSpace(command.Access) ? null : ParseAccess(command.Access),
                command.Description,
                cancellationToken)
            .ConfigureAwait(false);

        if (result.Data is { Success: true })
        {
            return VersionOperationResult.Success("Updated", "The branch version was updated on honua-server.");
        }

        return Failure(result.Issue, "The Honua server did not accept the alter-version request.");
    }

    public async Task<VersionOperationResult> DeleteVersionAsync(
        string serviceId,
        string versionGuid,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(versionGuid);

        var result = await _client.DeleteVersionAsync(serviceId, versionGuid, cancellationToken).ConfigureAwait(false);
        if (result.Data is { Success: true })
        {
            return VersionOperationResult.Success("Deleted", "The branch version was deleted on honua-server.");
        }

        return Failure(result.Issue, "The Honua server did not accept the delete-version request.");
    }

    public async Task<ReconcileResultView> ReconcileAsync(
        ReconcileVersionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var result = await _client
            .ReconcileAsync(
                command.ServiceId,
                command.VersionGuid,
                ParsePolicy(command.Policy),
                command.AbortIfConflicts,
                cancellationToken)
            .ConfigureAwait(false);

        // Gate on the server's success flag like the sibling alter/delete/post operations: a 200
        // response can still carry success:false (e.g. abortIfConflicts aborting because conflicts
        // exist, or the version being edited/locked). Reporting that as "Reconciled" would tell the
        // operator the reconcile ran when it did not.
        if (result.Data is { Success: true } reconcile)
        {
            var remaining = reconcile.Conflicts.Count;
            var detail = reconcile.CanPost
                ? $"Reconciled cleanly. {reconcile.AutoResolvedCount} conflict(s) auto-resolved; the version can be posted."
                : $"Reconciled with {remaining} remaining conflict(s) ({reconcile.AutoResolvedCount} auto-resolved). Resolve the remaining conflicts before posting.";

            return new ReconcileResultView
            {
                Operation = VersionOperationResult.Success(reconcile.CanPost ? "Reconciled" : "Conflicts", detail),
                HasConflicts = reconcile.HasConflicts,
                CanPost = reconcile.CanPost,
                AutoResolvedCount = reconcile.AutoResolvedCount,
                RemainingConflictCount = remaining
            };
        }

        if (result.Data is { Success: false } aborted)
        {
            var remaining = aborted.Conflicts.Count;
            var detail = aborted.HasConflicts
                ? $"honua-server did not complete the reconcile (success=false): {remaining} conflict(s) remain unresolved. Resolve the conflicts or relax the policy, then retry."
                : "honua-server reported the reconcile did not complete (success=false). The version may be locked or in edit.";

            return new ReconcileResultView
            {
                Operation = VersionOperationResult.Failure("Conflicts", detail),
                HasConflicts = aborted.HasConflicts,
                CanPost = aborted.CanPost,
                AutoResolvedCount = aborted.AutoResolvedCount,
                RemainingConflictCount = remaining
            };
        }

        return new ReconcileResultView
        {
            Operation = Failure(result.Issue, "The Honua server did not accept the reconcile request.")
        };
    }

    public async Task<VersionConflictsView> InspectConflictsAsync(
        string serviceId,
        string versionGuid,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(versionGuid);

        var result = await _client.InspectConflictsAsync(serviceId, versionGuid, cancellationToken).ConfigureAwait(false);
        if (result.Data is { } inspect)
        {
            return new VersionConflictsView
            {
                Bound = true,
                HasConflicts = inspect.HasConflicts,
                Conflicts = VersionConflictDiffBuilder.Build(inspect.Conflicts)
            };
        }

        var issue = result.Issue;
        return VersionConflictsView.Unbound(
            issue?.State ?? "Unavailable",
            issue?.Detail ?? "The Honua server did not return the pending conflict set for this version.",
            issue?.Contract);
    }

    public async Task<ResolveConflictsResultView> ResolveConflictsAsync(
        string serviceId,
        string versionGuid,
        IReadOnlyList<ConflictResolutionChoice> resolutions,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(versionGuid);
        ArgumentNullException.ThrowIfNull(resolutions);

        var wire = resolutions
            .Select(r => new HonuaVersionConflictResolution(r.LayerId, r.ObjectId, ParseChoice(r.Choice)))
            .ToArray();

        var result = await _client
            .ResolveConflictsAsync(serviceId, versionGuid, wire, cancellationToken)
            .ConfigureAwait(false);

        if (result.Data is { } resolve)
        {
            var detail = resolve.CanPost
                ? $"Resolved {resolve.Resolved} conflict(s); no conflicts remain. The version can be posted."
                : $"Resolved {resolve.Resolved} conflict(s); {resolve.Remaining} conflict(s) still block post.";

            return new ResolveConflictsResultView
            {
                Operation = VersionOperationResult.Success(resolve.CanPost ? "Resolved" : "Partially resolved", detail),
                Resolved = resolve.Resolved,
                Remaining = resolve.Remaining,
                CanPost = resolve.CanPost
            };
        }

        return new ResolveConflictsResultView
        {
            Operation = Failure(result.Issue, "The Honua server did not accept the resolve-conflicts request.")
        };
    }

    public async Task<VersionOperationResult> PostAsync(
        string serviceId,
        string versionGuid,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(versionGuid);

        var result = await _client.PostAsync(serviceId, versionGuid, cancellationToken).ConfigureAwait(false);
        if (result.Data is { } post)
        {
            if (post.BlockedByConflicts)
            {
                return VersionOperationResult.Failure(
                    "Blocked",
                    "Post was refused because unresolved conflicts remain. Resolve all conflicts before posting.");
            }

            if (post.Success)
            {
                return VersionOperationResult.Success(
                    "Posted",
                    $"Posted to DEFAULT. {post.AppliedChanges} change(s) applied (server generation {post.ServerGeneration}).");
            }

            return VersionOperationResult.Failure("Unavailable", "The Honua server did not commit the post.");
        }

        return Failure(result.Issue, "The Honua server did not accept the post request.");
    }

    private static VersionOperationResult Failure(HonuaAdminEndpointIssue? issue, string fallbackDetail) =>
        VersionOperationResult.Failure(
            issue?.State ?? "Unavailable",
            issue?.Detail ?? fallbackDetail);

    private static HonuaVersionReconcilePolicy ParsePolicy(string? policy) =>
        policy?.Trim().ToLowerInvariant() switch
        {
            "lastwritewins" or "last-write-wins" => HonuaVersionReconcilePolicy.LastWriteWins,
            "versionwins" or "version-wins" => HonuaVersionReconcilePolicy.VersionWins,
            "defaultwins" or "default-wins" => HonuaVersionReconcilePolicy.DefaultWins,
            _ => HonuaVersionReconcilePolicy.None
        };

    private static HonuaVersionConflictChoice ParseChoice(string? choice) =>
        choice?.Trim().ToLowerInvariant() switch
        {
            "default" or "takedefault" or "take-default" => HonuaVersionConflictChoice.TakeDefault,
            "base" or "takebase" or "take-base" => HonuaVersionConflictChoice.TakeBase,
            _ => HonuaVersionConflictChoice.TakeVersion
        };

    private static HonuaVersionAccess ParseAccess(string? access) =>
        access?.Trim().ToLowerInvariant() switch
        {
            "public" => HonuaVersionAccess.Public,
            "protected" => HonuaVersionAccess.Protected,
            _ => HonuaVersionAccess.Private
        };
}
