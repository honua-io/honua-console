using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// The console's layer subtypes + attribute-rules operation: reads and authors a layer's subtype set and
/// attribute rules on honua-server (<c>GET/PUT /api/v1/admin/metadata/layers/{layerId}/subtypes</c> and
/// <c>.../attribute-rules</c>). The live implementation is DI-gated on a configured server base URL;
/// otherwise the surface binds to <see cref="UnsupportedConsoleLayerSubtypesOperation"/> (missing-binding, no
/// network call). It never fabricates data (Console Patterns Charter section 11).
/// </summary>
public interface IConsoleLayerSubtypesOperation
{
    /// <summary>Reads the persisted subtype set for a layer (by its global layer id).</summary>
    Task<ConsoleLayerSubtypes> GetSubtypesAsync(int layerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves the layer's subtype set. When <paramref name="clear"/> is true the whole set is removed and the
    /// supplied rows are ignored.
    /// </summary>
    Task<ConsoleSetSubtypesResult> SetSubtypesAsync(
        int layerId,
        string? subtypeField,
        string? defaultSubtypeCode,
        bool clear,
        IReadOnlyList<ConsoleLayerSubtype> subtypes,
        CancellationToken cancellationToken = default);

    /// <summary>Reads the persisted attribute rules for a layer (by its global layer id).</summary>
    Task<ConsoleLayerAttributeRules> GetAttributeRulesAsync(int layerId, CancellationToken cancellationToken = default);

    /// <summary>Replaces the layer's attribute-rule set with the supplied rows (an empty set clears it).</summary>
    Task<ConsoleSetAttributeRulesResult> SetAttributeRulesAsync(
        int layerId,
        IReadOnlyList<ConsoleAttributeRule> rules,
        CancellationToken cancellationToken = default);
}
