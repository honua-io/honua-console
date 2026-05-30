using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Studio dashboard-builder data source. Every method binds to the server-owned honua-server dashboard
/// package lifecycle on the publication registry (honua-server#1183) through the Honua.Console.Contracts
/// shim; there is no standing in-memory dashboard client in the merged result (Console Patterns Charter
/// section 11). When no server binding is configured, the unsupported implementation surfaces an explicit
/// missing-binding state rather than fabricating dashboard data.
/// </summary>
public interface IStudioDashboardPackageDataSource
{
    /// <summary>Lists the server's dashboard packages plus any binding/permission capability states.</summary>
    Task<StudioDashboardWorkspace> GetWorkspaceAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads an existing package's current version into editor state, or returns a fresh draft template
    /// when <paramref name="dashboardId"/> is null/blank.
    /// </summary>
    Task<StudioDashboardEditorLoad> LoadAsync(string? dashboardId, CancellationToken cancellationToken = default);

    /// <summary>Creates or updates the draft for the supplied editor state.</summary>
    Task<StudioDashboardCommandResult> SaveDraftAsync(
        StudioDashboardEditorState state,
        CancellationToken cancellationToken = default);

    /// <summary>Runs server publish-validation against the saved draft version.</summary>
    Task<StudioDashboardCommandResult> ValidateAsync(
        StudioDashboardEditorState state,
        CancellationToken cancellationToken = default);

    /// <summary>Publishes the saved draft version after the pre-publish gate passes.</summary>
    Task<StudioDashboardCommandResult> PublishAsync(
        StudioDashboardEditorState state,
        CancellationToken cancellationToken = default);

    /// <summary>Reopens a published version as a new editable draft.</summary>
    Task<StudioDashboardCommandResult> ReopenAsync(
        string dashboardId,
        int version,
        CancellationToken cancellationToken = default);
}
