using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public interface IConsoleLayerFilterOperation
{
    Task<ConsoleLayerFilter> GetFilterAsync(int layerId, CancellationToken cancellationToken = default);

    Task<ConsoleSetLayerFilterResult> SaveFilterAsync(
        int layerId,
        string expression,
        string language,
        CancellationToken cancellationToken = default);

    Task<ConsoleSetLayerFilterResult> ClearFilterAsync(int layerId, CancellationToken cancellationToken = default);
}
