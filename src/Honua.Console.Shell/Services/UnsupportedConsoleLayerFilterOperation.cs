using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public sealed class UnsupportedConsoleLayerFilterOperation : IConsoleLayerFilterOperation
{
    private const string BindingDetail =
        "Configure Honua:Server:BaseUrl or HONUA_SERVER_BASE_URL so the console can read and author the layer's permanent filter on honua-server.";

    public Task<ConsoleLayerFilter> GetFilterAsync(int layerId, CancellationToken cancellationToken = default) =>
        Task.FromResult(ConsoleLayerFilter.Unbound(BindingDetail));

    public Task<ConsoleSetLayerFilterResult> SaveFilterAsync(
        int layerId,
        string expression,
        string language,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(ConsoleSetLayerFilterResult.MissingBinding(BindingDetail));

    public Task<ConsoleSetLayerFilterResult> ClearFilterAsync(
        int layerId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(ConsoleSetLayerFilterResult.MissingBinding(BindingDetail));
}
