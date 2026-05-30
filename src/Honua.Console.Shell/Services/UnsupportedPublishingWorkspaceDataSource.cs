using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Merged-runtime publishing data source used when no honua-server base URL is configured. It never
/// returns mock matrix or review data (Console Patterns Charter section 11): it surfaces an explicit
/// missing-binding capability state so the publishing workspace renders the same first-class
/// missing-binding surface as the rest of Operate. A server-bound source replaces this once the
/// service/layer publish projection (and the full publication registry, <c>honua-server#1183</c>)
/// are wired through <c>honua-sdk-dotnet</c> or the Console contracts shim.
/// </summary>
public sealed class UnsupportedPublishingWorkspaceDataSource : IPublishingWorkspaceDataSource
{
    private static readonly PublishingWorkspace Workspace = new(
        Matrix: [],
        Reviews: [],
        CapabilityStates:
        [
            new OperateCapabilityState(
                "Publishing",
                "Missing binding",
                "Honua:Server:BaseUrl",
                "Configure Honua:Server:BaseUrl or HONUA_SERVER_BASE_URL so the publishing workspace can "
                + "read service/layer publish state from honua-server. The full publication matrix and "
                + "registry bind once honua-server#1183 lands; no mock data source is merged in the interim.")
        ]);

    public Task<PublishingWorkspace> GetWorkspaceAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Workspace);
}
