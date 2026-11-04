using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public interface IConsoleServiceImportOperation
{
    Task<ConsoleServiceImportResult> DiscoverAsync(
        string serviceUrl,
        ConsoleServiceImportAuth? auth = null,
        CancellationToken cancellationToken = default);

    Task<ConsoleServiceImportRun> StartLayerImportAsync(
        ConsoleServiceImportRunRequest request,
        CancellationToken cancellationToken = default);

    Task<ConsoleServiceImportJob> GetImportJobAsync(
        string jobId,
        CancellationToken cancellationToken = default);
}
