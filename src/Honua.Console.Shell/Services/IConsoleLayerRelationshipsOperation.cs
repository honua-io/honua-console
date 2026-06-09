using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// The console's layer relationships operation: reads a layer's relationships and replaces the set on
/// honua-server (<c>GET/PUT /api/v1/admin/metadata/layers/{layerId}/relationships</c>). The live
/// implementation is DI-gated on a configured server base URL; otherwise the surface binds to
/// <see cref="UnsupportedConsoleLayerRelationshipsOperation"/> (missing-binding, no network call). It never
/// fabricates relationships (Console Patterns Charter section 11).
/// </summary>
public interface IConsoleLayerRelationshipsOperation
{
    /// <summary>Reads the persisted relationships for a layer (by its global layer id).</summary>
    Task<ConsoleLayerRelationships> GetRelationshipsAsync(int layerId, CancellationToken cancellationToken = default);

    /// <summary>Replaces the layer's relationship set with the supplied rows.</summary>
    Task<ConsoleSetRelationshipsResult> SetRelationshipsAsync(
        int layerId,
        IReadOnlyList<ConsoleLayerRelationship> relationships,
        CancellationToken cancellationToken = default);
}
