using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public interface IConsoleFileImportOperation
{
    Task<IReadOnlyList<string>> GetSupportedExtensionsAsync(CancellationToken cancellationToken = default);

    Task<ConsoleFileImportPreview> PreviewAsync(byte[] bytes, string fileName, CancellationToken cancellationToken = default);

    Task<OperateSectionResult<HonuaAdminCoordinateSystemSearch>> SearchCoordinateSystemsAsync(
        string query, string authorityBinding, CancellationToken cancellationToken = default);

    Task<ConsoleFileImportResult> ImportAsync(
        ConsoleFileImportCommand command,
        CancellationToken cancellationToken = default);
}
