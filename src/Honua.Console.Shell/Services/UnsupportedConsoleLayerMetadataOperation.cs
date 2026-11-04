using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public sealed class UnsupportedConsoleLayerMetadataOperation : IConsoleLayerMetadataOperation
{
    private const string BindingDetail =
        "Configure Honua:Server:BaseUrl or HONUA_SERVER_BASE_URL so the console can author layer editing policy on honua-server.";

    public Task<ConsoleSetLayerMetadataResult> SetEditingAsync(
        ConsoleLayerEditingChange change,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(ConsoleSetLayerMetadataResult.MissingBinding(BindingDetail));
}
