using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Query-builder data source used when no honua-server base address is configured. The workspace renders
/// an explicit missing-binding state instead of fabricating query packages, keeping the merged runtime
/// free of a standing in-memory query client (Console Patterns Charter section 11). The query authoring
/// surface stays bound to the real honua-server saved-query lifecycle (honua-server#1182) or shows this
/// state.
/// </summary>
public sealed class UnsupportedStudioQueryPackageDataSource : IStudioQueryPackageDataSource
{
    internal static readonly StudioQueryCapabilityState MissingBinding = new(
        "Query builder",
        "Missing binding",
        "Honua:Server:BaseUrl",
        "Configure Honua:Server:BaseUrl or HONUA_SERVER_BASE_URL so the query builder can bind the server-owned saved query content lifecycle from honua-server (#1182).");

    public Task<StudioQueryWorkspace> GetWorkspaceAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new StudioQueryWorkspace([], [MissingBinding]));
}
