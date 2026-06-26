using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Form-builder data source used when no honua-server base address is configured. Every surface renders
/// an explicit missing-binding state instead of fabricating form data, keeping the merged runtime free of
/// a standing in-memory form client (Console Patterns Charter section 11). The form authoring surface
/// stays bound to the real honua-server form package lifecycle (honua-server#1184) or shows this state.
/// </summary>
public sealed class UnsupportedStudioFormPackageDataSource : IStudioFormPackageDataSource
{
    private static readonly StudioFormCapabilityState MissingBinding = new(
        "Form builder",
        "Missing binding",
        "Honua:Server:BaseUrl",
        "Configure Honua:Server:BaseUrl or HONUA_SERVER_BASE_URL so the form builder can bind the server-owned form package lifecycle from honua-server.");

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

    public Task<StudioFormAiCapability> GetGenerationCapabilityAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult(StudioFormAiCapability.Blocked(MissingBinding));

    public Task<StudioFormGenerationOutcome> GenerateAsync(
        StudioFormEditorState currentState,
        StudioFormGenerationRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(StudioFormGenerationOutcome.Blocked(MissingBinding));

    public Task<StudioFormImportOutcome> ImportXlsFormAsync(
        StudioFormImportRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(StudioFormImportOutcome.Blocked(MissingBinding));

    private static StudioFormCommandResult BindingFailure() =>
        new(false, MissingBinding.Detail, Issue: MissingBinding);
}
