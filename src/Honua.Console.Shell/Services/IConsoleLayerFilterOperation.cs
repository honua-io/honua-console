using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// The console's layer permanent-filter operation: reads a layer's server-enforced query filter and authors
/// it on honua-server (<c>GET/PUT /api/v1/admin/metadata/layers/{layerId}/filter</c>). The live
/// implementation is DI-gated on a configured server base URL; otherwise the surface binds to
/// <see cref="UnsupportedConsoleLayerFilterOperation"/> (missing-binding, no network call). It never
/// fabricates success — a save/clear result reflects what the server read back, and a server-side validation
/// rejection (400) is surfaced verbatim (Console Patterns Charter section 11).
/// </summary>
public interface IConsoleLayerFilterOperation
{
    /// <summary>Reads the persisted permanent filter for a layer (by its global layer id).</summary>
    Task<ConsoleLayerFilter> GetFilterAsync(int layerId, CancellationToken cancellationToken = default);

    /// <summary>Saves a layer's permanent filter expression in the given language.</summary>
    Task<ConsoleSetLayerFilterResult> SaveFilterAsync(
        int layerId,
        string expression,
        string language,
        CancellationToken cancellationToken = default);

    /// <summary>Clears the layer's saved permanent filter (sends <c>{ permanentFilter: null }</c>).</summary>
    Task<ConsoleSetLayerFilterResult> ClearFilterAsync(int layerId, CancellationToken cancellationToken = default);
}
