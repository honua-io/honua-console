using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Studio map-builder data source. Every method binds to the server-owned honua-server map package
/// lifecycle (honua-server#1180, closed) and the publication registry (honua-server#1183, closed)
/// through the Honua.Console.Contracts shim; there is no standing in-memory map client in the merged
/// result (Console Patterns Charter section 11). When no server binding is configured, the unsupported
/// implementation surfaces an explicit missing-binding state rather than fabricating map data.
/// </summary>
public interface IStudioMapPackageDataSource
{
    /// <summary>Lists the server's map packages plus any binding/permission capability states.</summary>
    Task<StudioMapWorkspace> GetWorkspaceAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads an existing package's current version into editor state, or returns a fresh draft
    /// template when <paramref name="mapId"/> is null/blank.
    /// </summary>
    Task<StudioMapEditorLoad> LoadAsync(string? mapId, CancellationToken cancellationToken = default);

    /// <summary>Creates or updates the draft for the supplied editor state.</summary>
    Task<StudioMapCommandResult> SaveDraftAsync(
        StudioMapEditorState state,
        CancellationToken cancellationToken = default);

    /// <summary>Publishes the saved draft version after the pre-publish gate passes.</summary>
    Task<StudioMapCommandResult> PublishAsync(
        StudioMapEditorState state,
        CancellationToken cancellationToken = default);

    /// <summary>Reopens a published version as a new editable draft.</summary>
    Task<StudioMapCommandResult> ReopenAsync(
        string mapId,
        int version,
        CancellationToken cancellationToken = default);
}
