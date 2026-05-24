using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public sealed class InMemoryConsoleAccountSessionStore : IConsoleAccountSessionStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, ConsoleAccountSession> _sessions = new(StringComparer.Ordinal);

    public Task<ConsoleAccountSession?> GetSessionAsync(
        string profileId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            _sessions.TryGetValue(profileId, out var session);
            return Task.FromResult(session);
        }
    }

    public Task SaveSessionAsync(ConsoleAccountSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(session.ProfileId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            _sessions[session.ProfileId] = session;
        }

        return Task.CompletedTask;
    }

    public Task ClearSessionAsync(string profileId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            _sessions.Remove(profileId);
        }

        return Task.CompletedTask;
    }
}
