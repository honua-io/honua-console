using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public sealed class InMemoryConsoleAccountSessionStore : IConsoleAccountSessionStore, IConsoleAccountSessionSnapshotStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, SessionSlot> _sessions = new(StringComparer.Ordinal);

    public Task<ConsoleAccountSession?> GetSessionAsync(
        string profileId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            _sessions.TryGetValue(profileId, out var slot);
            return Task.FromResult(slot?.Session);
        }
    }

    public Task SaveSessionAsync(ConsoleAccountSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(session.ProfileId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            _sessions[session.ProfileId] = new SessionSlot(session);
        }

        return Task.CompletedTask;
    }

    public Task ClearSessionAsync(string profileId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            // Removing this profile's slot invalidates its snapshots, including an absent session,
            // without changing other profiles or retaining a tombstone for the cleared profile.
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
            if (!_sessions.TryGetValue(profileId, out var slot))
            {
                slot = new SessionSlot(null);
                _sessions.Add(profileId, slot);
            }
            return Task.FromResult(new ConsoleAccountSessionSnapshot(profileId, slot.Session, slot));
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
            _sessions[session.ProfileId] = new SessionSlot(session);
            return Task.FromResult(true);
        }
    }

    private bool IsCurrentLocked(ConsoleAccountSessionSnapshot snapshot)
    {
        return _sessions.TryGetValue(snapshot.ProfileId, out var current)
            && ReferenceEquals(snapshot.Owner, current);
    }

    private sealed class SessionSlot(ConsoleAccountSession? session)
    {
        public ConsoleAccountSession? Session { get; } = session;
    }
}
