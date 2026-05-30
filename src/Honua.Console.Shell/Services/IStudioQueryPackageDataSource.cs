using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Studio query-builder data source. The query builder (/studio/query, honua-console#52) binds to the
/// server-owned saved query content/version lifecycle landed in honua-server#1182 ("Saved query and
/// analysis content versions with job artifacts", AnalysisContentKind.SavedQuery) through the
/// Honua.Console.Contracts shim; there is no standing in-memory query client in the merged result (Console
/// Patterns Charter section 11). When no server binding is configured, the unsupported implementation
/// surfaces an explicit missing-binding state rather than fabricating query packages.
///
/// Every method maps the authored query (source binding, predicate builder, projection, parameters) onto
/// the real saved-query content document and reads the map/table preview from the live server preview
/// route. The generated SQL/filter readout is produced Console-side from the same editor state for review
/// before save.
/// </summary>
public interface IStudioQueryPackageDataSource
{
    /// <summary>Lists the server's saved query packages plus any binding/permission capability states.</summary>
    Task<StudioQueryWorkspace> GetWorkspaceAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads an existing query's current version into editor state, or returns a fresh draft template when
    /// <paramref name="queryId"/> is null/blank.
    /// </summary>
    Task<StudioQueryEditorLoad> LoadAsync(string? queryId, CancellationToken cancellationToken = default);

    /// <summary>Creates or updates the saved query as a new content item/version.</summary>
    Task<StudioQueryCommandResult> SaveAsync(StudioQueryEditor query, CancellationToken cancellationToken = default);

    /// <summary>Runs the saved query through the live server preview route for the map/table preview.</summary>
    Task<StudioQueryCommandResult> PreviewAsync(StudioQueryEditor query, CancellationToken cancellationToken = default);
}
