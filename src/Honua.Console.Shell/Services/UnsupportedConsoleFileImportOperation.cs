using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public sealed class UnsupportedConsoleFileImportOperation : IConsoleFileImportOperation
{
    public Task<IReadOnlyList<string>> GetSupportedExtensionsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<string>>([]);

    public Task<ConsoleFileImportResult> ImportAsync(
        ConsoleFileImportCommand command,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(ConsoleFileImportResult.MissingBinding(
            "Configure Honua:Server:BaseUrl or HONUA_SERVER_BASE_URL so the console can import files to honua-server."));
}
