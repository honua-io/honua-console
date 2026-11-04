using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public interface IStudioFormPackageDataSource
{
    Task<StudioFormWorkspace> GetWorkspaceAsync(CancellationToken cancellationToken = default);

    Task<StudioFormEditorLoad> LoadAsync(string? formId, CancellationToken cancellationToken = default);

    Task<StudioFormCommandResult> SaveDraftAsync(
        StudioFormEditorState state,
        CancellationToken cancellationToken = default);

    Task<StudioFormCommandResult> ValidateAsync(
        StudioFormEditorState state,
        CancellationToken cancellationToken = default);

    Task<StudioFormCommandResult> PublishAsync(
        StudioFormEditorState state,
        CancellationToken cancellationToken = default);

    Task<StudioFormCommandResult> ReopenAsync(
        string formId,
        int version,
        CancellationToken cancellationToken = default);

    Task<StudioFormOfflinePolicyView> GetOfflinePolicyAsync(
        string formId,
        CancellationToken cancellationToken = default);

}
