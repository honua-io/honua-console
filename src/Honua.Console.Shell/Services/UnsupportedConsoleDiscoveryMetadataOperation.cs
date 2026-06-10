using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Missing-binding implementation of the discovery-metadata authoring operation. Used when no honua-server
/// base URL is configured: it performs no network call and returns explicit missing-binding results.
/// </summary>
public sealed class UnsupportedConsoleDiscoveryMetadataOperation : IConsoleDiscoveryMetadataOperation
{
    private const string BindingDetail =
        "Configure Honua:Server:BaseUrl or HONUA_SERVER_BASE_URL so the console can read and author discovery / catalog metadata on honua-server.";

    public Task<ConsoleDiscoveryMetadata> GetLayerDiscoveryAsync(int layerId, CancellationToken cancellationToken = default) =>
        Task.FromResult(ConsoleDiscoveryMetadata.Unbound(BindingDetail));

    public Task<ConsoleSaveDiscoveryResult> SaveLayerDiscoveryAsync(
        int layerId,
        ConsoleDiscoveryMetadata metadata,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(ConsoleSaveDiscoveryResult.MissingBinding(BindingDetail));

    public Task<ConsoleDiscoveryMetadata> GetServiceDiscoveryAsync(string serviceName, CancellationToken cancellationToken = default) =>
        Task.FromResult(ConsoleDiscoveryMetadata.Unbound(BindingDetail));

    public Task<ConsoleSaveDiscoveryResult> SaveServiceDiscoveryAsync(
        string serviceName,
        ConsoleDiscoveryMetadata metadata,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(ConsoleSaveDiscoveryResult.MissingBinding(BindingDetail));
}
