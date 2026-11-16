using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public sealed class UnsupportedOperateTransitionDataSource : IOperateTransitionDataSource
{
    private static readonly OperateTransitionWorkspace Workspace = new(
        Connections: [],
        DatasetEdits: [],
        Services: [],
        SettingsChanges: [],
        CapabilityStates:
        [
            new OperateCapabilityState(
                "Operate",
                "Missing binding",
                "Honua:Server:BaseUrl",
                "Configure Honua:Server:BaseUrl or HONUA_SERVER_BASE_URL so Operate can read server-owned admin data from honua-server.")
        ]);

    public Task<OperateTransitionWorkspace> GetWorkspaceAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Workspace);

    public Task<OperateConnectionSummary?> FindConnectionAsync(
        string connectionId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<OperateConnectionSummary?>(null);

    public Task<OperateDatasetEditPreview?> FindDatasetEditAsync(
        string resourceId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<OperateDatasetEditPreview?>(null);

    public Task<OperateServiceDetail?> FindServiceAsync(
        string serviceName,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<OperateServiceDetail?>(null);
}
