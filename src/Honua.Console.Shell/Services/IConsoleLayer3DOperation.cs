using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// The console's layer 3D-extrusion / 3D-symbology + lifecycle-status authoring operation: reads and writes a
/// layer's 3D extrusion + 3D symbology (<c>GET/PUT /api/v1/admin/metadata/layers/{layerId}/extrusion</c>) and
/// its lifecycle status (<c>GET/PUT /api/v1/admin/metadata/layers/{layerId}/status</c>) on honua-server. The
/// live implementation is DI-gated on a configured server base URL; otherwise the surface binds to
/// <see cref="UnsupportedConsoleLayer3DOperation"/> (missing-binding, no network call). It never fabricates
/// metadata (Console Patterns Charter section 11).
/// </summary>
public interface IConsoleLayer3DOperation
{
    /// <summary>Reads a layer's 3D extrusion + 3D symbology metadata (by its global layer id).</summary>
    Task<ConsoleLayer3D> GetExtrusionAsync(int layerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves a layer's 3D extrusion + 3D symbology. A null section leaves it unchanged; the matching clear flag
    /// removes it server-side.
    /// </summary>
    Task<ConsoleSetLayerMetadataResult> SetExtrusionAsync(
        int layerId,
        ConsoleLayerExtrusionSettings? extrusion,
        bool clearExtrusion,
        ConsoleSymbology3D? symbology3D,
        bool clearSymbology3D,
        CancellationToken cancellationToken = default);

    /// <summary>Reads a layer's lifecycle status (by its global layer id).</summary>
    Task<ConsoleLayerStatus> GetStatusAsync(int layerId, CancellationToken cancellationToken = default);

    /// <summary>Saves a layer's lifecycle status; a null field leaves the other unchanged server-side.</summary>
    Task<ConsoleSetLayerMetadataResult> SetStatusAsync(
        int layerId,
        string? lifecycle,
        string? state,
        CancellationToken cancellationToken = default);
}
