using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public interface IConsoleLayerRelationshipsOperation
{
    Task<ConsoleLayerRelationships> GetRelationshipsAsync(int layerId, CancellationToken cancellationToken = default);

    Task<ConsoleSetRelationshipsResult> SetRelationshipsAsync(
        int layerId,
        IReadOnlyList<ConsoleLayerRelationship> relationships,
        CancellationToken cancellationToken = default);
}
