using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Studio query-builder data source. The query builder (/studio/query, honua-console#52) binds to the
/// server-owned saved query content/version lifecycle landed in honua-server#1182 ("Saved query and
/// analysis content versions with job artifacts") through the Honua.Console.Contracts shim; there is no
/// standing in-memory query client in the merged result (Console Patterns Charter section 11). When no
/// server binding is configured, the unsupported implementation surfaces an explicit missing-binding
/// state rather than fabricating query packages.
///
/// This first slice exposes the workspace read so the page can render the bound package list or the
/// missing-binding surface. The draft load / predicate-build / generated-SQL readout / parameter editor /
/// map+table preview / save-as-content commands follow on the same seam once honua-server#1182's wire
/// shape is projected into the shim (or honua-sdk-dotnet ships a consumable projection).
/// </summary>
public interface IStudioQueryPackageDataSource
{
    /// <summary>Lists the server's saved query packages plus any binding/permission capability states.</summary>
    Task<StudioQueryWorkspace> GetWorkspaceAsync(CancellationToken cancellationToken = default);
}
