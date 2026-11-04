using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

// This contract edits the automation body the Collect engine runs; the in-memory client is test-only and is not the deployed source.
public interface ICollectAutomationClient
{
    // Newest first, or an empty list when the surface is unbound.
    Task<IReadOnlyList<CollectAutomationSummary>> ListAutomationsAsync(CancellationToken cancellationToken = default);

    // A null or "new" id opens a local draft; an unbound surface returns a binding state.
    Task<CollectAutomationEditorContext> OpenEditorAsync(string? draftId, CancellationToken cancellationToken = default);

    // A validation failure returns issues and no version id and leaves the draft dirty; a valid body commits an immutable version and advances it monotonically.
    Task<CollectAutomationSaveResult> SaveVersionAsync(
        CollectAutomationDraft draft,
        string changeNote,
        CancellationToken cancellationToken = default);

    // Newest first; a never-saved draft has an empty history, and an unbound surface returns a binding state.
    Task<CollectAutomationVersionHistory> ListVersionsAsync(
        string contentItemId,
        CancellationToken cancellationToken = default);

    // Null when the version cannot be resolved or the surface is unbound.
    Task<CollectAutomationDraft?> GetVersionAsync(
        string contentItemId,
        string versionId,
        CancellationToken cancellationToken = default);

    // Restore appends a new version equal to the chosen body and never mutates the prior version.
    Task<CollectAutomationRestoreResult> RestoreVersionAsync(
        string contentItemId,
        string versionId,
        CancellationToken cancellationToken = default);
}
