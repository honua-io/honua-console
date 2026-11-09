using Honua.Sdk.Admin.Models;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public sealed class HonuaServerServiceLayerPublishOperation : IServiceLayerPublishOperation
{
    private readonly IHonuaAdminOperateClient _client;
    private readonly IConsoleAuthorityBindingValidator? _authority;

    public HonuaServerServiceLayerPublishOperation(IHonuaAdminOperateClient client, IConsoleAuthorityBindingValidator? authority = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _authority = authority;
    }

    public async Task<ServiceLayerPublishResult> PublishAsync(
        ServiceLayerPublishCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.FolderId.IsSpecified && string.IsNullOrWhiteSpace(command.AuthorityBinding))
            return new() { State = "Target unverified", Detail = "Refresh Folders and review the publishing target before choosing a Folder or Root." };

        if (command.AuthorityBinding is { } reviewedBinding &&
            (_authority is null || !await _authority.IsCurrentAsync(reviewedBinding, cancellationToken)))
            return new() { State = "Target changed", Detail = "Your user or environment changed. Review the original publishing target." };

        var request = new PublishLayerRequest
        {
            FolderId = command.FolderId,
            PublicName = command.PublicName,
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

        HonuaAdminEndpointResult<HonuaAdminPublishedLayerSummary> result;
        try
        {
            result = command.AuthorityBinding is { } binding
                ? await _client.PublishLayerBoundAsync(command.ConnectionId, request, binding, cancellationToken).ConfigureAwait(false)
                : await _client.PublishLayerAsync(command.ConnectionId, request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException)
        {
            return new() { State = ConsoleWriteOutcome.UnverifiedState, Detail = ConsoleWriteOutcome.UnverifiedDetail };
        }
        if (command.AuthorityBinding is { } sentBinding && _authority is not null &&
            !await _authority.IsCurrentAsync(sentBinding, CancellationToken.None))
            return new() { State = ConsoleWriteOutcome.UnverifiedState, Detail = ConsoleWriteOutcome.UnverifiedDetail };


        if (command.AuthorityBinding is not null && result.Issue is { State: "Unavailable", StatusCode: null } transportIssue)
            return new() { State = ConsoleWriteOutcome.UnverifiedState, Detail = ConsoleWriteOutcome.UnverifiedDetail + " " + transportIssue.Detail };

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

    public async Task<IReadOnlyList<ServiceLayerPublishTable>> ListTablesAsync(
        string connectionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);

        var result = await _client.ListConnectionTablesAsync(connectionId, cancellationToken).ConfigureAwait(false);
        if (result.Data is not { } tables)
        {
            return [];
        }

        return tables
            .Where(table => !string.IsNullOrWhiteSpace(table.Table))
            .Select(table => new ServiceLayerPublishTable
            {
                Schema = table.Schema ?? "public",
                Table = table.Table!,
                GeometryColumn = table.GeometryColumn,
                GeometryType = table.GeometryType,
                Srid = table.Srid,
                EstimatedRows = table.EstimatedRows,
                Columns = (table.Columns ?? [])
                    .Select(column => column.Name)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Select(name => name!)
                    .ToArray()
            })
            .ToArray();
    }
}
