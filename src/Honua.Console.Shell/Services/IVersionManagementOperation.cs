using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public interface IVersionManagementOperation
{
    Task<OperateVersionListView> ListVersionsAsync(
        string serviceId,
        CancellationToken cancellationToken = default);

    Task<VersionOperationResult> CreateVersionAsync(
        CreateVersionCommand command,
        CancellationToken cancellationToken = default);

    Task<VersionOperationResult> AlterVersionAsync(
        AlterVersionCommand command,
        CancellationToken cancellationToken = default);

    Task<VersionOperationResult> DeleteVersionAsync(
        string serviceId,
        string versionGuid,
        CancellationToken cancellationToken = default);

    Task<ReconcileResultView> ReconcileAsync(
        ReconcileVersionCommand command,
        CancellationToken cancellationToken = default);

    Task<VersionConflictsView> InspectConflictsAsync(
        string serviceId,
        string versionGuid,
        CancellationToken cancellationToken = default);

    Task<ResolveConflictsResultView> ResolveConflictsAsync(
        string serviceId,
        string versionGuid,
        IReadOnlyList<ConflictResolutionChoice> resolutions,
        CancellationToken cancellationToken = default);

    // Post is refused while conflicts remain.
    Task<VersionOperationResult> PostAsync(
        string serviceId,
        string versionGuid,
        CancellationToken cancellationToken = default);
}

public sealed record ConflictResolutionChoice(int LayerId, long ObjectId, string Choice);

public sealed record ResolveConflictsResultView
{
    public required VersionOperationResult Operation { get; init; }

    public int Resolved { get; init; }

    public int Remaining { get; init; }

    public bool CanPost { get; init; }
}
