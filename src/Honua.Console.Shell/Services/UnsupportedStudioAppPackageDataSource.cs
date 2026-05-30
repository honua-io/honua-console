using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// App-builder data source used when no honua-server base address is configured. Every surface renders
/// an explicit missing-binding state instead of fabricating app data, keeping the merged runtime free
/// of a standing in-memory app client (Console Patterns Charter section 11). The app authoring surface
/// stays bound to the real honua-server Studio package lifecycle + app publication registry
/// (honua-server#1180/#1181/#1183) or shows this state. Mirrors
/// <see cref="UnsupportedStudioFormPackageDataSource"/>.
/// </summary>
public sealed class UnsupportedStudioAppPackageDataSource : IStudioAppPackageDataSource
{
    private static readonly StudioAppCapabilityState MissingBinding = new(
        "App builder",
        "Missing binding",
        "Honua:Server:BaseUrl",
        "Configure Honua:Server:BaseUrl or HONUA_SERVER_BASE_URL so the app builder can bind the server-owned Studio package lifecycle and app publication registry from honua-server.");

    public Task<StudioAppEditorLoad> LoadAsync(Guid? draftId, CancellationToken cancellationToken = default) =>
        Task.FromResult(new StudioAppEditorLoad(null, [MissingBinding]));

    public Task<StudioAppCommandResult> SaveDraftAsync(
        StudioAppEditorState state,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(BindingFailure());

    public Task<StudioAppCommandResult> ValidateAsync(
        StudioAppEditorState state,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(BindingFailure());

    public Task<StudioAppCommandResult> PublishAsync(
        StudioAppEditorState state,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(BindingFailure());

    public Task<StudioAppCommandResult> PreviewAsync(
        StudioAppEditorState state,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(BindingFailure());

    public Task<StudioAppVersionHistory> LoadVersionHistoryAsync(
        Guid itemId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new StudioAppVersionHistory(itemId, [], MissingBinding));

    public Task<StudioAppCommandResult> ReopenAsync(
        Guid itemId,
        Guid versionId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(BindingFailure());

    public Task<StudioAppCommandResult> RollbackAsync(
        Guid itemId,
        Guid targetVersionId,
        string? reason,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(BindingFailure());

    private static StudioAppCommandResult BindingFailure() =>
        new(false, MissingBinding.Detail, Issue: MissingBinding);
}
