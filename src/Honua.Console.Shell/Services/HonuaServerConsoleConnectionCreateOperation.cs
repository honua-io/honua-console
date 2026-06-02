using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Live implementation of the connection-create operation. POSTs the operator's intent to honua-server's
/// admin connections endpoint through <see cref="IHonuaAdminOperateClient"/> and maps the server result
/// (or rejection) into a <see cref="ConsoleConnectionCreateResult"/>. This is the real create the Add
/// Connection form drives — it never fabricates success.
/// </summary>
public sealed class HonuaServerConsoleConnectionCreateOperation : IConsoleConnectionCreateOperation
{
    private readonly IHonuaAdminOperateClient _client;

    public HonuaServerConsoleConnectionCreateOperation(IHonuaAdminOperateClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public async Task<ConsoleConnectionCreateResult> CreateAsync(
        ConsoleConnectionCreateCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var result = await _client
            .CreateConnectionAsync(ToRequest(command), cancellationToken)
            .ConfigureAwait(false);

        if (result.Data is { } connection)
        {
            return new ConsoleConnectionCreateResult
            {
                Succeeded = true,
                State = "Created",
                Detail = "The connection is persisted on honua-server and ready for resources and services.",
                ConnectionId = connection.ConnectionId == Guid.Empty
                    ? null
                    : connection.ConnectionId.ToString(),
                Name = connection.Name ?? command.Name
            };
        }

        var issue = result.Issue;
        return new ConsoleConnectionCreateResult
        {
            Succeeded = false,
            State = issue?.State ?? "Unavailable",
            Detail = issue?.Detail ?? "The Honua server did not accept the connection-create request.",
            FieldErrors = MapFieldErrors(issue)
        };
    }

    public async Task<ConsoleConnectionTestOutcome> TestAsync(
        ConsoleConnectionCreateCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var result = await _client
            .TestDraftConnectionAsync(ToRequest(command), cancellationToken)
            .ConfigureAwait(false);

        return MapTestOutcome(result);
    }

    public async Task<ConsoleConnectionTestOutcome> TestExistingAsync(
        string connectionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);

        var result = await _client
            .TestConnectionAsync(connectionId, cancellationToken)
            .ConfigureAwait(false);

        return MapTestOutcome(result);
    }

    private static ConsoleConnectionTestOutcome MapTestOutcome(
        HonuaAdminEndpointResult<HonuaAdminConnectionTestResult> result)
    {
        if (result.Data is { } test)
        {
            return new ConsoleConnectionTestOutcome
            {
                Tested = true,
                IsHealthy = test.IsHealthy,
                State = test.IsHealthy ? "Healthy" : "Unhealthy",
                Detail = test.Message ?? (test.IsHealthy
                    ? "honua-server opened the connection successfully."
                    : "honua-server could not open the connection with the supplied target and credentials.")
            };
        }

        var issue = result.Issue;
        return new ConsoleConnectionTestOutcome
        {
            Tested = false,
            IsHealthy = false,
            State = issue?.State ?? "Unavailable",
            Detail = issue?.Detail ?? "The Honua server did not accept the connection-test request.",
            FieldErrors = MapFieldErrors(issue)
        };
    }

    private static HonuaAdminCreateConnectionRequest ToRequest(ConsoleConnectionCreateCommand command) => new()
    {
        Name = command.Name,
        Description = command.Description,
        Host = command.Host,
        Port = command.Port,
        DatabaseName = command.DatabaseName,
        Username = command.Username,
        Password = command.Password,
        Provider = command.Provider,
        SslRequired = command.SslRequired,
        SslMode = command.SslMode
    };

    private static IReadOnlyList<ConsoleConnectionCreateFieldError> MapFieldErrors(HonuaAdminEndpointIssue? issue) =>
        (issue?.FieldErrors ?? [])
            .Select(error => new ConsoleConnectionCreateFieldError(
                error.Code,
                error.Message,
                error.Path,
                error.FieldId,
                error.Severity))
            .ToArray();
}
