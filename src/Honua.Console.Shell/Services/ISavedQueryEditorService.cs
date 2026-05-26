using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Drives the Studio saved-query editor (<c>/studio/query</c>). Binds the saved-query content
/// item/version lifecycle and preview to honua-server's Analysis Content API (#1182) through the
/// <see cref="Honua.Console.Contracts.IAnalysisContentClient"/> shim. The predicate builder and the
/// generated filter readout are Console-local UX (<see cref="SavedQueryContentMapper"/>); no
/// server-owned query data is mocked. When the server is not configured, the unsupported implementation
/// returns a missing-binding state so the editor renders the shared blocked surface.
/// </summary>
public interface ISavedQueryEditorService
{
    /// <summary>
    /// The missing-binding state when no honua-server base address is configured, or <c>null</c> when the
    /// editor is bound. Lets the page render the blocked surface without a startup network call.
    /// </summary>
    SavedQueryBindingState? GetBindingStatus();

    /// <summary>Creates a saved-query content item and its first immutable version.</summary>
    Task<SavedQuerySaveResult> CreateAsync(
        SavedQueryDefinitionDraft definition,
        CancellationToken cancellationToken = default);

    /// <summary>Reopens an existing saved-query item by id at its latest version.</summary>
    Task<SavedQueryOpenResult> OpenAsync(
        string itemId,
        CancellationToken cancellationToken = default);

    /// <summary>Saves the current edits as a new immutable version of an existing item.</summary>
    Task<SavedQuerySaveResult> SaveNewVersionAsync(
        string itemId,
        string? basedOnVersionId,
        SavedQueryDefinitionDraft definition,
        CancellationToken cancellationToken = default);

    /// <summary>Runs a server preview of a saved-query version (table rows + map summary).</summary>
    Task<SavedQueryPreviewOutcome> PreviewAsync(
        string itemId,
        int contentVersion,
        int? limit,
        CancellationToken cancellationToken = default);
}
