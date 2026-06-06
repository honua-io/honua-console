using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// The console's layer field-configuration operation: reads a layer's fields and authors coded-value domains
/// on honua-server (<c>GET/PUT /api/v1/admin/metadata/layers/{layerId}/fields</c>). The live implementation is
/// DI-gated on a configured server base URL; otherwise the surface binds to
/// <see cref="UnsupportedConsoleLayerFieldsOperation"/> (missing-binding, no network call).
/// </summary>
public interface IConsoleLayerFieldsOperation
{
    /// <summary>Reads the persisted field configuration for a layer (by its global layer id).</summary>
    Task<ConsoleLayerFields> GetFieldsAsync(int layerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets (or, with an empty <paramref name="codedValues"/> list, clears) a coded-value domain on a field.
    /// </summary>
    Task<ConsoleSetDomainResult> SetCodedValueDomainAsync(
        int layerId,
        string fieldName,
        string domainName,
        IReadOnlyList<ConsoleCodedValue> codedValues,
        CancellationToken cancellationToken = default);
}
