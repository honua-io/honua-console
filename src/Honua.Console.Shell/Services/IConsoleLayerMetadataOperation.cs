using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// The console's layer-metadata authoring operation: reads and writes a layer's display hints,
/// editor-tracking / edit-capability metadata, and spatial/CRS metadata on honua-server
/// (<c>GET/PUT /api/v1/admin/metadata/layers/{layerId}/display|editing|spatial</c>). The live implementation
/// is DI-gated on a configured server base URL; otherwise the surface binds to
/// <see cref="UnsupportedConsoleLayerMetadataOperation"/> (missing-binding, no network call). It never
/// fabricates metadata (Console Patterns Charter section 11).
/// </summary>
public interface IConsoleLayerMetadataOperation
{
    /// <summary>Reads a layer's display hints (by its global layer id).</summary>
    Task<ConsoleLayerDisplay> GetDisplayAsync(int layerId, CancellationToken cancellationToken = default);

    /// <summary>Saves a layer's display hints; null fields are left unchanged server-side.</summary>
    Task<ConsoleSetLayerMetadataResult> SetDisplayAsync(
        int layerId,
        ConsoleLayerDisplay display,
        CancellationToken cancellationToken = default);

    /// <summary>Reads a layer's editor-tracking / edit-capability metadata (by its global layer id).</summary>
    Task<ConsoleLayerEditing> GetEditingAsync(int layerId, CancellationToken cancellationToken = default);

    /// <summary>Saves a layer's editor-tracking / edit-capability metadata; null fields are left unchanged.</summary>
    Task<ConsoleSetLayerMetadataResult> SetEditingAsync(
        int layerId,
        ConsoleLayerEditing editing,
        CancellationToken cancellationToken = default);

    /// <summary>Reads a layer's spatial/CRS metadata (by its global layer id).</summary>
    Task<ConsoleLayerSpatial> GetSpatialAsync(int layerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves a layer's CRS-list/output spatial metadata. <paramref name="supportedCrs"/> null = leave the list
    /// unchanged, an empty list = clear it. The clear flags clear the storage-CRS scalar output fields.
    /// </summary>
    Task<ConsoleSetLayerMetadataResult> SetSpatialAsync(
        int layerId,
        IReadOnlyList<string>? supportedCrs,
        string? storageCrs,
        double? storageCrsCoordinateEpoch,
        bool clearStorageCrs,
        bool clearStorageCrsCoordinateEpoch,
        CancellationToken cancellationToken = default);
}
