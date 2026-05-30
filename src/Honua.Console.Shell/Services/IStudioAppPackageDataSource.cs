using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Studio app-builder data source (<c>/studio/app</c>, honua-console#58). Every method binds to the
/// server-owned honua-server Studio package lifecycle and the app publication registry
/// (honua-server#1180/#1181/#1183) through the <c>Honua.Console.Contracts</c> shim; there is no
/// standing in-memory app client in the merged result (Console Patterns Charter section 11). When no
/// server binding is configured, the unsupported implementation surfaces an explicit missing-binding
/// state rather than fabricating app data. Mirrors <see cref="IStudioFormPackageDataSource"/>.
/// </summary>
public interface IStudioAppPackageDataSource
{
    /// <summary>
    /// Opens an app draft. With no <paramref name="draftId"/> the server creates a fresh app.package
    /// draft from a blank Console scaffold; an existing draft id reloads the live draft.
    /// </summary>
    Task<StudioAppEditorLoad> LoadAsync(Guid? draftId, CancellationToken cancellationToken = default);

    /// <summary>Saves the supplied editor state into the server-owned app draft (create or update).</summary>
    Task<StudioAppCommandResult> SaveDraftAsync(
        StudioAppEditorState state,
        CancellationToken cancellationToken = default);

    /// <summary>Runs server publish-validation against the saved app draft.</summary>
    Task<StudioAppCommandResult> ValidateAsync(
        StudioAppEditorState state,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves the validated draft as an immutable content version and submits a publication request to
    /// the app publication registry. Reopened edits create new content versions rather than mutating
    /// the published version.
    /// </summary>
    Task<StudioAppCommandResult> PublishAsync(
        StudioAppEditorState state,
        CancellationToken cancellationToken = default);
}
