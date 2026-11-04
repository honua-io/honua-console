using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public sealed class UnsupportedVersionManagementOperation : IVersionManagementOperation
{
    private const string MissingBindingDetail =
        "Configure Honua:Server:BaseUrl or HONUA_SERVER_BASE_URL so the console can manage branch versions on honua-server.";

    public Task<OperateVersionListView> ListVersionsAsync(
        string serviceId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(OperateVersionListView.Unbound("Missing binding", MissingBindingDetail));

    public Task<VersionOperationResult> CreateVersionAsync(
        CreateVersionCommand command,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(VersionOperationResult.MissingBinding(MissingBindingDetail));

    public Task<VersionOperationResult> AlterVersionAsync(
        AlterVersionCommand command,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(VersionOperationResult.MissingBinding(MissingBindingDetail));

    public Task<VersionOperationResult> DeleteVersionAsync(
        string serviceId,
        string versionGuid,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(VersionOperationResult.MissingBinding(MissingBindingDetail));

    public Task<ReconcileResultView> ReconcileAsync(
        ReconcileVersionCommand command,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new ReconcileResultView
        {
            Operation = VersionOperationResult.MissingBinding(MissingBindingDetail)
        });

    public Task<VersionConflictsView> InspectConflictsAsync(
        string serviceId,
        string versionGuid,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(VersionConflictsView.Unbound("Missing binding", MissingBindingDetail));

    public Task<ResolveConflictsResultView> ResolveConflictsAsync(
        string serviceId,
        string versionGuid,
        IReadOnlyList<ConflictResolutionChoice> resolutions,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new ResolveConflictsResultView
        {
            Operation = VersionOperationResult.MissingBinding(MissingBindingDetail)
        });

    public Task<VersionOperationResult> PostAsync(
        string serviceId,
        string versionGuid,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(VersionOperationResult.MissingBinding(MissingBindingDetail));
}
