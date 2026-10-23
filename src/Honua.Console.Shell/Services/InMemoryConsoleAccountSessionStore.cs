using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public sealed class InMemoryConsoleAccountSessionStore : IConsoleAccountSessionStore, IConsoleAccountSessionSnapshotStore
{
    private readonly object _gate = new();
    private long _generation;
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
            // Invalidate every previously captured write, including a snapshot of an absent session.
            _generation++;
            _sessions.Remove(profileId);
        }

        return Task.CompletedTask;
    }

    public Task<ConsoleAccountSessionSnapshot> CaptureSessionAsync(
        string profileId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _sessions.TryGetValue(profileId, out var session);
            return Task.FromResult(new ConsoleAccountSessionSnapshot(profileId, session, this, _generation));
        }
    }

    public Task<bool> IsCurrentSessionAsync(
        ConsoleAccountSessionSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult(IsCurrentLocked(snapshot));
        }
    }

    public Task<bool> TrySaveSessionAsync(
        ConsoleAccountSessionSnapshot snapshot, ConsoleAccountSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(session);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!string.Equals(snapshot.ProfileId, session.ProfileId, StringComparison.Ordinal)
                || !IsCurrentLocked(snapshot))
            {
                return Task.FromResult(false);
            }
            _sessions[session.ProfileId] = session;
            return Task.FromResult(true);
        }
    }

    private bool IsCurrentLocked(ConsoleAccountSessionSnapshot snapshot)
    {
        _sessions.TryGetValue(snapshot.ProfileId, out var current);
        return ReferenceEquals(snapshot.Owner, this)
            && snapshot.Generation == _generation
            && ReferenceEquals(current, snapshot.Session);
    }
}
