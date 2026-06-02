using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Live implementation of the file-import operation. Reads the server's supported formats and uploads the
/// file through <see cref="IHonuaAdminOperateClient"/> to honua-server's streamed import endpoint, mapping
/// the server result (or rejection) into a <see cref="ConsoleFileImportResult"/>. It never fabricates success.
/// </summary>
public sealed class HonuaServerConsoleFileImportOperation : IConsoleFileImportOperation
{
    private readonly IHonuaAdminOperateClient _client;

    public HonuaServerConsoleFileImportOperation(IHonuaAdminOperateClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public async Task<IReadOnlyList<string>> GetSupportedExtensionsAsync(CancellationToken cancellationToken = default)
    {
        var result = await _client.GetImportFormatsAsync(cancellationToken).ConfigureAwait(false);
        return result.Data?.SupportedExtensions ?? [];
    }

    public async Task<ConsoleFileImportResult> ImportAsync(
        ConsoleFileImportCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var result = await _client
            .ImportFileAsync(command.FileContent, command.FileName, command.TableName, command.TargetSchema, cancellationToken)
            .ConfigureAwait(false);

        if (result.Data is { } data)
        {
            if (data.Success)
            {
                return new ConsoleFileImportResult
                {
                    Succeeded = true,
                    State = "Imported",
                    Detail = $"Imported {data.FeatureCount} feature(s) from {data.Format ?? "the file"} into the new table. Publish it from the publishing workspace to make it a live layer.",
                    TableName = data.TableName,
                    FeatureCount = data.FeatureCount,
                };
            }

            // The endpoint returns HTTP 200 with success=false on a failed import; surface its reason.
            return new ConsoleFileImportResult
            {
                Succeeded = false,
                State = "Rejected",
                Detail = data.ErrorMessage ?? "The Honua server could not import the file.",
                TableName = data.TableName,
                ValidationErrors = data.ValidationErrors,
            };
        }

        var issue = result.Issue;
        return new ConsoleFileImportResult
        {
            Succeeded = false,
            State = issue?.State ?? "Unavailable",
            Detail = issue?.Detail ?? "The Honua server did not accept the file-import request.",
        };
    }
}
