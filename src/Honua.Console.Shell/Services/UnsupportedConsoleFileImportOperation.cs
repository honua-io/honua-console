using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public sealed class UnsupportedConsoleFileImportOperation : IConsoleFileImportOperation
{
    public Task<IReadOnlyList<string>> GetSupportedExtensionsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<string>>([]);

    public Task<ConsoleFileImportPreview> PreviewAsync(byte[] bytes, string fileName, CancellationToken cancellationToken = default) =>
        Task.FromResult(ConsoleFileImportPreview.MissingBinding("Connect to a server before checking the file."));

    public Task<OperateSectionResult<HonuaAdminCoordinateSystemSearch>> SearchCoordinateSystemsAsync(
        string query, string authorityBinding, CancellationToken cancellationToken = default) =>
        Task.FromResult(OperateSectionResult<HonuaAdminCoordinateSystemSearch>.Denied(
            OperateSectionStatus.Unavailable, "Connect to a server before searching coordinate systems."));

    public Task<ConsoleFileImportResult> ImportAsync(
        ConsoleFileImportCommand command,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(ConsoleFileImportResult.MissingBinding(
            "Configure Honua:Server:BaseUrl or HONUA_SERVER_BASE_URL so the console can import files to honua-server."));
}
