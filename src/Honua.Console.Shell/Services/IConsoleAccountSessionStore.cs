using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public interface IConsoleAccountSessionStore
{
    Task<ConsoleAccountSession?> GetSessionAsync(string profileId, CancellationToken cancellationToken = default);

    Task SaveSessionAsync(ConsoleAccountSession session, CancellationToken cancellationToken = default);

    Task ClearSessionAsync(string profileId, CancellationToken cancellationToken = default);
}
