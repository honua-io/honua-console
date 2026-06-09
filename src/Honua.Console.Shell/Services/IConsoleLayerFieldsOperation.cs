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

    /// <summary>
    /// Authors a field's domain (coded-value or range), its merge/split policies, and a per-field default value
    /// through the same <c>PUT /api/v1/admin/metadata/layers/{layerId}/fields</c> the coded-value editor uses.
    /// All authoring is sent in one field update so the round-trip reflects the combined state.
    /// </summary>
    /// <remarks>
    /// Defaulted so pre-existing fakes/implementations that only authored coded-value domains keep compiling;
    /// the live <see cref="HonuaServerConsoleLayerFieldsOperation"/> and missing-binding
    /// <c>UnsupportedConsoleLayerFieldsOperation</c> both override it.
    /// </remarks>
    Task<ConsoleSetDomainResult> SetDomainAsync(
        int layerId,
        ConsoleDomainAuthoring authoring,
        CancellationToken cancellationToken = default) =>
        SetCodedValueDomainAsync(
            layerId,
            authoring.FieldName,
            authoring.DomainName ?? authoring.FieldName,
            authoring.Kind == ConsoleDomainKind.CodedValue ? authoring.CodedValues : [],
            cancellationToken);

    /// <summary>
    /// Sets a field's display <paramref name="alias"/> and <paramref name="hidden"/> visibility through the same
    /// <c>PUT /api/v1/admin/metadata/layers/{layerId}/fields</c> the domain editor uses. A null/empty alias is
    /// sent as-is (the server treats it as "no override"); the field's domain is left untouched.
    /// </summary>
    Task<ConsoleSetDomainResult> SetFieldConfigurationAsync(
        int layerId,
        string fieldName,
        string? alias,
        bool hidden,
        CancellationToken cancellationToken = default);
}
