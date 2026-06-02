using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// The console's file-import OPERATION: uploads a geospatial file to honua-server's streamed import endpoint
/// (<c>POST /api/v1/admin/import/upload</c>) to create a PostgreSQL table, and exposes the server's supported
/// formats so the Import UI can validate a file's extension before uploading. The live implementation is
/// DI-gated on a configured server base URL; otherwise the surface binds to
/// <see cref="UnsupportedConsoleFileImportOperation"/> (missing-binding, no network call).
/// </summary>
public interface IConsoleFileImportOperation
{
    /// <summary>The geospatial file extensions the server can import (e.g. <c>.geojson</c>, <c>.gpkg</c>, <c>.zip</c>).</summary>
    Task<IReadOnlyList<string>> GetSupportedExtensionsAsync(CancellationToken cancellationToken = default);

    /// <summary>Uploads and imports the file, returning the resulting table or an explicit failure.</summary>
    Task<ConsoleFileImportResult> ImportAsync(
        ConsoleFileImportCommand command,
        CancellationToken cancellationToken = default);
}
