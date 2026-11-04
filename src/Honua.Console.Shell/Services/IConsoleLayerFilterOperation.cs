using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

// A save or clear is success only when the server returns the stored filter, and a 400 validation rejection is surfaced verbatim.
public interface IConsoleLayerFilterOperation
{
    Task<ConsoleLayerFilter> GetFilterAsync(int layerId, CancellationToken cancellationToken = default);

    Task<ConsoleSetLayerFilterResult> SaveFilterAsync(
        int layerId,
        string expression,
        string language,
        CancellationToken cancellationToken = default);

    // Clear sends { "permanentFilter": null }, which removes the saved filter.
    Task<ConsoleSetLayerFilterResult> ClearFilterAsync(int layerId, CancellationToken cancellationToken = default);
}
