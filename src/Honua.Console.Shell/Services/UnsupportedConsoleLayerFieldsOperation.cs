using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public sealed class UnsupportedConsoleLayerFieldsOperation : IConsoleLayerFieldsOperation
{
    private const string BindingDetail =
        "Configure Honua:Server:BaseUrl or HONUA_SERVER_BASE_URL so the console can read and author layer field domains on honua-server.";

    public Task<ConsoleLayerFields> GetFieldsAsync(int layerId, CancellationToken cancellationToken = default) =>
        Task.FromResult(ConsoleLayerFields.Unbound(BindingDetail));

    public Task<ConsoleSetDomainResult> SetCodedValueDomainAsync(
        int layerId,
        string fieldName,
        string domainName,
        IReadOnlyList<ConsoleCodedValue> codedValues,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(ConsoleSetDomainResult.MissingBinding(BindingDetail));

    public Task<ConsoleSetDomainResult> SetDomainAsync(
        int layerId,
        ConsoleDomainAuthoring authoring,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(ConsoleSetDomainResult.MissingBinding(BindingDetail));

    public Task<ConsoleSetDomainResult> SetFieldConfigurationAsync(
        int layerId,
        string fieldName,
        string? alias,
        bool hidden,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(ConsoleSetDomainResult.MissingBinding(BindingDetail));
}
