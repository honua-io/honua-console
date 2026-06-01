using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Live implementation of the service-layer-publish operation. POSTs the operator's intent to honua-server's
/// admin layer-publishing endpoint through <see cref="IHonuaAdminOperateClient"/> and maps the server result
/// (or rejection) into a <see cref="ServiceLayerPublishResult"/>. This is the real publish the publishing
/// wizard's finish action drives (issue #144) — it never fabricates success.
/// </summary>
public sealed class HonuaServerServiceLayerPublishOperation : IServiceLayerPublishOperation
{
    private readonly IHonuaAdminOperateClient _client;

    public HonuaServerServiceLayerPublishOperation(IHonuaAdminOperateClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public async Task<ServiceLayerPublishResult> PublishAsync(
        ServiceLayerPublishCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var request = new HonuaAdminPublishLayerRequest
        {
            Schema = command.Schema,
            Table = command.Table,
            LayerName = command.LayerName,
            Description = command.Description,
            GeometryColumn = command.GeometryColumn,
            GeometryType = command.GeometryType,
            Srid = command.Srid,
            PrimaryKey = command.PrimaryKey,
            Fields = command.Fields,
            ServiceName = command.ServiceName,
            Enabled = command.Enabled
        };

        var result = await _client
            .PublishLayerAsync(command.ConnectionId, request, cancellationToken)
            .ConfigureAwait(false);

        if (result.Data is { } layer)
        {
            return new ServiceLayerPublishResult
            {
                Succeeded = true,
                State = layer.Enabled == false ? "Disabled" : "Published",
                Detail = "The layer is live on honua-server and queryable through its FeatureServer slot.",
                LayerId = layer.LayerId,
                LayerName = layer.LayerName,
                ServiceName = layer.ServiceName ?? command.ServiceName,
                GeometryType = layer.GeometryType,
                Srid = layer.Srid,
                Enabled = layer.Enabled
            };
        }

        var issue = result.Issue;
        return new ServiceLayerPublishResult
        {
            Succeeded = false,
            State = issue?.State ?? "Unavailable",
            Detail = issue?.Detail ?? "The Honua server did not accept the layer-publish request.",
            FieldErrors = (issue?.FieldErrors ?? [])
                .Select(error => new ServiceLayerPublishFieldError(
                    error.Code,
                    error.Message,
                    error.Path,
                    error.FieldId,
                    error.Severity))
                .ToArray()
        };
    }
}
