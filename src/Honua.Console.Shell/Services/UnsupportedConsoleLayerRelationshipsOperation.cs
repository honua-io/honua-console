using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public sealed class UnsupportedConsoleLayerRelationshipsOperation : IConsoleLayerRelationshipsOperation
{
    private const string BindingDetail =
        "Configure Honua:Server:BaseUrl or HONUA_SERVER_BASE_URL so the console can read and author layer relationships on honua-server.";

    public Task<ConsoleLayerRelationships> GetRelationshipsAsync(int layerId, CancellationToken cancellationToken = default) =>
        Task.FromResult(ConsoleLayerRelationships.Unbound(BindingDetail));

    public Task<ConsoleSetRelationshipsResult> SetRelationshipsAsync(
        int layerId,
        IReadOnlyList<ConsoleLayerRelationship> relationships,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(ConsoleSetRelationshipsResult.MissingBinding(BindingDetail));
}
