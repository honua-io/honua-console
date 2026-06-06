using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Missing-binding implementation of the file-import operation. Used when no honua-server base URL is
/// configured: it performs no network call and returns an explicit missing-binding result so the Import UI
/// surfaces the binding requirement instead of fabricating an import (Console Patterns Charter section 11).
/// </summary>
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
