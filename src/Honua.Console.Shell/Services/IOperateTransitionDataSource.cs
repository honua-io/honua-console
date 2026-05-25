using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public interface IOperateTransitionDataSource
{
    Task<OperateTransitionWorkspace> GetWorkspaceAsync(CancellationToken cancellationToken = default);

    Task<OperateConnectionSummary?> FindConnectionAsync(
        string connectionId,
        CancellationToken cancellationToken = default);

    Task<OperateResourceEditPreview?> FindResourceEditAsync(
        string resourceId,
        CancellationToken cancellationToken = default);

    Task<OperateServiceDetail?> FindServiceAsync(
        string serviceName,
        CancellationToken cancellationToken = default);
}
