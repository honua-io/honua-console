using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public sealed class UnsupportedStudioFormPackageDataSource : IStudioFormPackageDataSource
{
    private static readonly StudioFormCapabilityState MissingBinding = new(
        "Form builder",
        "Missing binding",
        "Honua:Server:BaseUrl",
        "Configure Honua:Server:BaseUrl or HONUA_SERVER_BASE_URL so the form builder can bind the server-owned form lifecycle from honua-server.");

    public Task<StudioFormWorkspace> GetWorkspaceAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new StudioFormWorkspace([], [MissingBinding]));

    public Task<StudioFormEditorLoad> LoadAsync(string? formId, CancellationToken cancellationToken = default) =>
        Task.FromResult(new StudioFormEditorLoad(null, [MissingBinding]));

    public Task<StudioFormCommandResult> SaveDraftAsync(
        StudioFormEditorState state,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(BindingFailure());

    public Task<StudioFormCommandResult> ValidateAsync(
        StudioFormEditorState state,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(BindingFailure());

    public Task<StudioFormCommandResult> PublishAsync(
        StudioFormEditorState state,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(BindingFailure());

    public Task<StudioFormCommandResult> ReopenAsync(
        string formId,
        int version,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(BindingFailure());

    public Task<StudioFormOfflinePolicyView> GetOfflinePolicyAsync(
        string formId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new StudioFormOfflinePolicyView(false, [], MissingBinding));

    private static StudioFormCommandResult BindingFailure() =>
        new(false, MissingBinding.Detail, Issue: MissingBinding);
}
