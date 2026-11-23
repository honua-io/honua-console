using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public interface IConsoleActivityFeedClient
{
    Task<OperateSectionResult<ConsoleActivityFeedPage>> ReadAsync(
        ConsoleActivityFeedQuery query,
        CancellationToken cancellationToken = default);
}

public sealed class UnsupportedConsoleActivityFeedClient : IConsoleActivityFeedClient
{
    public Task<OperateSectionResult<ConsoleActivityFeedPage>> ReadAsync(
        ConsoleActivityFeedQuery query,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(OperateSectionResult<ConsoleActivityFeedPage>.Denied(
            OperateSectionStatus.Unsupported,
            "This Console has no server connection that records changes."));
}
