using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public sealed class UnsupportedConsoleConnectionCreateOperation : IConsoleConnectionCreateOperation
{
    public Task<ConsoleConnectionProviderAvailability> GetProviderAvailabilityAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult(ConsoleConnectionProviderAvailability.Unavailable with
        {
            UnavailableReason = "Configure Honua:Server:BaseUrl or HONUA_SERVER_BASE_URL so Console can read this server's installed database providers."
        });

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
