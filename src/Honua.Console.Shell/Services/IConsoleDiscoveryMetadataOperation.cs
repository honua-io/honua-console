using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// The console's discovery-metadata authoring operation: reads and writes the discovery / catalog metadata
/// (title, description, keywords, themes, license, attribution, publisher, contact, links) of a layer
/// (<c>GET/PUT /api/v1/admin/metadata/layers/{id}/discovery</c>) or a service
/// (<c>GET/PUT /api/v1/admin/services/{svc}/discovery</c>) on honua-server. This metadata drives the OGC API
/// Records / STAC / DCAT / Esri documentInfo output. The live implementation is DI-gated on a configured
/// server base URL; otherwise the surface binds to <see cref="UnsupportedConsoleDiscoveryMetadataOperation"/>
/// (missing-binding, no network call). It never fabricates metadata (Console Patterns Charter section 11).
/// </summary>
public interface IConsoleDiscoveryMetadataOperation
{
    /// <summary>Reads the discovery metadata for a layer (by its global layer id).</summary>
    Task<ConsoleDiscoveryMetadata> GetLayerDiscoveryAsync(int layerId, CancellationToken cancellationToken = default);

    /// <summary>Writes the discovery metadata for a layer (by its global layer id).</summary>
    Task<ConsoleSaveDiscoveryResult> SaveLayerDiscoveryAsync(
        int layerId,
        ConsoleDiscoveryMetadata metadata,
        CancellationToken cancellationToken = default);

    /// <summary>Reads the discovery metadata for a service (by name).</summary>
    Task<ConsoleDiscoveryMetadata> GetServiceDiscoveryAsync(string serviceName, CancellationToken cancellationToken = default);

    /// <summary>Writes the discovery metadata for a service (by name).</summary>
    Task<ConsoleSaveDiscoveryResult> SaveServiceDiscoveryAsync(
        string serviceName,
        ConsoleDiscoveryMetadata metadata,
        CancellationToken cancellationToken = default);
}
