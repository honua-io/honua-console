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

    // Surface-scoped reads. The default derives each view from the full workspace so in-memory
    // sources stay trivial; server-backed sources override these to fetch only the endpoints a
    // single route renders and avoid route-level network waterfalls.

    async Task<OperateConnectionsView> GetConnectionsViewAsync(CancellationToken cancellationToken = default)
    {
        var workspace = await GetWorkspaceAsync(cancellationToken).ConfigureAwait(false);
        return new OperateConnectionsView(workspace.Connections, workspace.CapabilityStates);
    }

    async Task<OperateResourcesView> GetResourcesViewAsync(CancellationToken cancellationToken = default)
    {
        var workspace = await GetWorkspaceAsync(cancellationToken).ConfigureAwait(false);
        return new OperateResourcesView(workspace.ResourceEdits, workspace.CapabilityStates);
    }

    // One dataset by id; server-backed sources read only that dataset's connection.
    Task<OperateResourcesView> GetResourceViewAsync(string resourceId, CancellationToken cancellationToken = default) =>
        GetResourcesViewAsync(cancellationToken);

    async Task<OperateServicesView> GetServicesViewAsync(CancellationToken cancellationToken = default)
    {
        var workspace = await GetWorkspaceAsync(cancellationToken).ConfigureAwait(false);
        return new OperateServicesView(workspace.Services, workspace.CapabilityStates);
    }

    // Summary-only service list for surfaces that show service names, protocols, layer counts,
    // and endpoints without per-service settings or per-connection layer reads.
    Task<OperateServicesView> GetServiceDirectoryViewAsync(CancellationToken cancellationToken = default) =>
        GetServicesViewAsync(cancellationToken);

    Task<OperateServicesView> GetServiceViewAsync(string serviceName, CancellationToken cancellationToken = default) =>
        GetServicesViewAsync(cancellationToken);

    async Task<OperateDataView> GetDataViewAsync(CancellationToken cancellationToken = default)
    {
        var workspace = await GetWorkspaceAsync(cancellationToken).ConfigureAwait(false);
        return new OperateDataView(workspace.ResourceEdits, new OperateServicesView(workspace.Services, workspace.CapabilityStates));
    }

    async Task<OperateServicesView> GetLayersViewAsync(CancellationToken cancellationToken = default)
    {
        var workspace = await GetWorkspaceAsync(cancellationToken).ConfigureAwait(false);
        return new OperateServicesView(workspace.Services, workspace.CapabilityStates);
    }

    Task<OperateServicesView> GetLayerViewAsync(string resourceId, CancellationToken cancellationToken = default) =>
        GetLayersViewAsync(cancellationToken);

    async Task<OperateSettingsView> GetSettingsViewAsync(CancellationToken cancellationToken = default)
    {
        var workspace = await GetWorkspaceAsync(cancellationToken).ConfigureAwait(false);
        return new OperateSettingsView(workspace.SettingsChanges, workspace.CapabilityStates);
    }
}
