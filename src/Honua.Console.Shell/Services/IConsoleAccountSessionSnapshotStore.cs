using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

// Conditional writes must not undo a concurrent invalidation.
public interface IConsoleAccountSessionSnapshotStore
{
    Task<ConsoleAccountSessionSnapshot> CaptureSessionAsync(string profileId, CancellationToken cancellationToken = default);

    Task<bool> IsCurrentSessionAsync(ConsoleAccountSessionSnapshot snapshot, CancellationToken cancellationToken = default);

    // Clears only while the captured profile slot is still current.
    Task<bool> TryClearSessionAsync(ConsoleAccountSessionSnapshot snapshot, CancellationToken cancellationToken = default);

    // Writes only while the captured session and storage identity are still current.
    Task<bool> TrySaveSessionAsync(ConsoleAccountSessionSnapshot snapshot, ConsoleAccountSession session,
        CancellationToken cancellationToken = default);
}

// The profile slot identity stays private to storage.
public sealed class ConsoleAccountSessionSnapshot
{
    internal ConsoleAccountSessionSnapshot(string profileId, ConsoleAccountSession? session, object owner)
    {
        ProfileId = profileId;
        Session = session;
        Owner = owner;
    }

    public ConsoleAccountSession? Session { get; }

    internal string ProfileId { get; }
    internal object Owner { get; }
}
