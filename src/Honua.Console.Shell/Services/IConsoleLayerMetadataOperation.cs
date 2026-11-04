using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public interface IConsoleLayerMetadataOperation
{
    Task<ConsoleSetLayerMetadataResult> SetEditingAsync(
        ConsoleLayerEditingChange change,
        CancellationToken cancellationToken = default);
}
