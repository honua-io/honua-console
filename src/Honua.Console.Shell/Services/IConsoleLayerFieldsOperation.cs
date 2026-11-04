using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public interface IConsoleLayerFieldsOperation
{
    Task<ConsoleLayerFields> GetFieldsAsync(int layerId, CancellationToken cancellationToken = default);

    Task<ConsoleSetDomainResult> SetCodedValueDomainAsync(
        int layerId,
        string fieldName,
        string domainName,
        IReadOnlyList<ConsoleCodedValue> codedValues,
        CancellationToken cancellationToken = default);

    // The default body only forwards coded values so older implementations still compile. The live client rejects a default-value change as unsupported.
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

    Task<ConsoleSetDomainResult> SetFieldConfigurationAsync(
        int layerId,
        string fieldName,
        string? alias,
        bool hidden,
        CancellationToken cancellationToken = default);
}
