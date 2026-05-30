using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Reads the Operate publishing workspace (publication matrix and review records) for
/// <c>/operate/publishing</c>. The merged runtime binds this to honua-server (service/layer publish
/// today, the full publication registry behind <c>honua-server#1183</c>) via <c>honua-sdk-dotnet</c>
/// or the <c>Honua.Console.Contracts</c> shim; there is no standing in-memory implementation in the
/// merged build. When no server base URL is configured the data source returns a workspace whose
/// only content is a missing-binding <see cref="OperateCapabilityState"/>.
/// </summary>
public interface IPublishingWorkspaceDataSource
{
    Task<PublishingWorkspace> GetWorkspaceAsync(CancellationToken cancellationToken = default);
}
