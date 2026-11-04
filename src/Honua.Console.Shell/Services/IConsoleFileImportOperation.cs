using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public interface IConsoleFileImportOperation
{
    Task<IReadOnlyList<string>> GetSupportedExtensionsAsync(CancellationToken cancellationToken = default);

    Task<ConsoleFileImportResult> ImportAsync(
        ConsoleFileImportCommand command,
        CancellationToken cancellationToken = default);
}
