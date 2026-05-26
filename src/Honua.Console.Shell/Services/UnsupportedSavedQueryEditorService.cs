using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// The runtime default when no honua-server base address is configured. Every operation returns a
/// missing-binding state so the saved-query editor renders the shared blocked surface (charter §11)
/// instead of mock query data. Mirrors <see cref="UnsupportedStudioAuthoringShell"/> and
/// <see cref="UnsupportedOperateTransitionDataSource"/>.
/// </summary>
public sealed class UnsupportedSavedQueryEditorService : ISavedQueryEditorService
{
    private static readonly SavedQueryBindingState MissingBinding = new(
        "Missing binding",
        "Honua:Server:BaseUrl",
        "Configure Honua:Server:BaseUrl or HONUA_SERVER_BASE_URL so Studio can author server-owned saved queries through the honua-server Analysis Content API.");

    public SavedQueryBindingState? GetBindingStatus() => MissingBinding;

    public Task<SavedQuerySaveResult> CreateAsync(
        SavedQueryDefinitionDraft definition,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(SavedQuerySaveResult.Blocked(MissingBinding));

    public Task<SavedQueryOpenResult> OpenAsync(
        string itemId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(SavedQueryOpenResult.Blocked(MissingBinding));

    public Task<SavedQuerySaveResult> SaveNewVersionAsync(
        string itemId,
        string? basedOnVersionId,
        SavedQueryDefinitionDraft definition,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(SavedQuerySaveResult.Blocked(MissingBinding));

    public Task<SavedQueryPreviewOutcome> PreviewAsync(
        string itemId,
        int contentVersion,
        int? limit,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(SavedQueryPreviewOutcome.Blocked(MissingBinding));
}
