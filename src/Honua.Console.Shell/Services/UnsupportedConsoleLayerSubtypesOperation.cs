using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Missing-binding implementation of the layer subtypes + attribute-rules operation. Used when no honua-server
/// base URL is configured: it performs no network call and returns explicit missing-binding results.
/// </summary>
public sealed class UnsupportedConsoleLayerSubtypesOperation : IConsoleLayerSubtypesOperation
{
    private const string BindingDetail =
        "Configure Honua:Server:BaseUrl or HONUA_SERVER_BASE_URL so the console can read and author layer subtypes and attribute rules on honua-server.";

    public Task<ConsoleLayerSubtypes> GetSubtypesAsync(int layerId, CancellationToken cancellationToken = default) =>
        Task.FromResult(ConsoleLayerSubtypes.Unbound(BindingDetail));

    public Task<ConsoleSetSubtypesResult> SetSubtypesAsync(
        int layerId,
        string? subtypeField,
        string? defaultSubtypeCode,
        bool clear,
        IReadOnlyList<ConsoleLayerSubtype> subtypes,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(ConsoleSetSubtypesResult.MissingBinding(BindingDetail));

    public Task<ConsoleLayerAttributeRules> GetAttributeRulesAsync(int layerId, CancellationToken cancellationToken = default) =>
        Task.FromResult(ConsoleLayerAttributeRules.Unbound(BindingDetail));

    public Task<ConsoleSetAttributeRulesResult> SetAttributeRulesAsync(
        int layerId,
        IReadOnlyList<ConsoleAttributeRule> rules,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(ConsoleSetAttributeRulesResult.MissingBinding(BindingDetail));
}
