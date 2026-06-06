using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Missing-binding implementation of the service-import operation. Used when no honua-server base URL is
/// configured: it performs no network call and returns an explicit missing-binding result so the service
/// import UI surfaces the binding requirement instead of fabricating discovery (Console Patterns Charter section 11).
/// </summary>
public sealed class UnsupportedConsoleServiceImportOperation : IConsoleServiceImportOperation
{
    private const string BindingDetail =
        "Configure Honua:Server:BaseUrl or HONUA_SERVER_BASE_URL so the console can discover remote services through honua-server.";

    public Task<ConsoleServiceImportResult> DiscoverAsync(
        string serviceUrl,
        ConsoleServiceImportAuth? auth = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(ConsoleServiceImportResult.MissingBinding(BindingDetail));

    public Task<ConsoleServiceImportRun> StartLayerImportAsync(
        ConsoleServiceImportRunRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new ConsoleServiceImportRun
        {
            Succeeded = false,
            State = "Missing binding",
            Detail = BindingDetail,
        });

    public Task<ConsoleServiceImportJob> GetImportJobAsync(
        string jobId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new ConsoleServiceImportJob
        {
            JobId = jobId,
            Status = "Missing binding",
            Terminal = true,
            Succeeded = false,
            Detail = BindingDetail,
        });
}
