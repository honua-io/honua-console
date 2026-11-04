using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public sealed class HonuaServerConsoleLayerMetadataOperation : IConsoleLayerMetadataOperation
{
    private readonly IHonuaAdminOperateClient _client;

    public HonuaServerConsoleLayerMetadataOperation(IHonuaAdminOperateClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public async Task<ConsoleSetLayerMetadataResult> SetEditingAsync(
        ConsoleLayerEditingChange change,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);

        var request = new HonuaAdminUpdateLayerMetadataRequest
        {
            Editing = new HonuaAdminLayerEditingPatch
            {
                GlobalIdField = change.GlobalIdField,
                SupportsAttachments = change.SupportsAttachments,
                Create = change.AllowCreate,
                Update = change.AllowUpdate,
                Delete = change.AllowDelete,
            },
        };
        var result = await _client
            .UpdateServiceLayerMetadataAsync(new HonuaAdminServiceLayer(change.ServiceName, change.LayerId), request, cancellationToken)
            .ConfigureAwait(false);

        return result.Data is not null
            ? new ConsoleSetLayerMetadataResult
            {
                Succeeded = true,
                State = "Updated",
                Detail = $"Saved the layer's editing policy for service '{change.ServiceName}' on honua-server.",
            }
            : new ConsoleSetLayerMetadataResult
            {
                Succeeded = false,
                State = result.Issue?.State ?? "Unavailable",
                Detail = result.Issue?.Detail ?? "The Honua server did not accept the editing policy update.",
            };
    }
}
