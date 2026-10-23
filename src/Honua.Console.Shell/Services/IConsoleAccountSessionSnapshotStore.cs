using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Supports account-session reads and conditional writes that cannot undo a concurrent invalidation.
/// </summary>
public interface IConsoleAccountSessionSnapshotStore
{
    /// <summary>Captures the current session and its opaque invalidation version.</summary>
    Task<ConsoleAccountSessionSnapshot> CaptureSessionAsync(string profileId, CancellationToken cancellationToken = default);

    /// <summary>Checks that a captured session still belongs to the current store and version.</summary>
    Task<bool> IsCurrentSessionAsync(ConsoleAccountSessionSnapshot snapshot, CancellationToken cancellationToken = default);

    /// <summary>Writes only while the captured session and invalidation version remain current.</summary>
    Task<bool> TrySaveSessionAsync(ConsoleAccountSessionSnapshot snapshot, ConsoleAccountSession session,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// An account-session snapshot whose store identity and invalidation version remain private to storage.
/// </summary>
public sealed class ConsoleAccountSessionSnapshot
{
    internal ConsoleAccountSessionSnapshot(string profileId, ConsoleAccountSession? session, object owner, long generation)
    {
        ProfileId = profileId;
        Session = session;
        Owner = owner;
        Generation = generation;
    }

    /// <summary>The session observed when the snapshot was captured, or null when absent.</summary>
    public ConsoleAccountSession? Session { get; }

    internal string ProfileId { get; }
    internal object Owner { get; }
    internal long Generation { get; }
}
