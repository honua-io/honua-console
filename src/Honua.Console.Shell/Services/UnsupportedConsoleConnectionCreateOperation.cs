using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Missing-binding implementation of the connection-create operation. Used when no honua-server base URL is
/// configured: it performs no network call and returns an explicit missing-binding result so the Add
/// Connection form surfaces the binding requirement instead of fabricating a create (Console Patterns Charter
/// section 11).
/// </summary>
public sealed class UnsupportedConsoleConnectionCreateOperation : IConsoleConnectionCreateOperation
{
    public Task<ConsoleConnectionCreateResult> CreateAsync(
        ConsoleConnectionCreateCommand command,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(ConsoleConnectionCreateResult.MissingBinding(
            "Configure Honua:Server:BaseUrl or HONUA_SERVER_BASE_URL so the console can create connections on honua-server."));

    public Task<ConsoleConnectionTestOutcome> TestAsync(
        ConsoleConnectionCreateCommand command,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(ConsoleConnectionTestOutcome.MissingBinding(
            "Configure Honua:Server:BaseUrl or HONUA_SERVER_BASE_URL so the console can test connections against honua-server."));

    public Task<ConsoleConnectionTestOutcome> TestExistingAsync(
        string connectionId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(ConsoleConnectionTestOutcome.MissingBinding(
            "Configure Honua:Server:BaseUrl or HONUA_SERVER_BASE_URL so the console can test connections against honua-server."));
}
