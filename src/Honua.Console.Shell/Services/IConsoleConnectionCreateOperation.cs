using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public interface IConsoleConnectionCreateOperation
{
    Task<ConsoleConnectionProviderAvailability> GetProviderAvailabilityAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult(ConsoleConnectionProviderAvailability.Unavailable);

    Task<ConsoleConnectionCreateResult> CreateAsync(
        ConsoleConnectionCreateCommand command,
        CancellationToken cancellationToken = default);

    // Tests the draft against the server and does not persist it.
    Task<ConsoleConnectionTestOutcome> TestAsync(
        ConsoleConnectionCreateCommand command,
        CancellationToken cancellationToken = default);

    // Tests a saved connection and the server persists the resulting health.
    Task<ConsoleConnectionTestOutcome> TestExistingAsync(
        string connectionId,
        CancellationToken cancellationToken = default);
}
