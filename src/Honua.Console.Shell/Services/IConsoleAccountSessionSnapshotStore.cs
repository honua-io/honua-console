using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Supports account-session reads and conditional writes that cannot undo a concurrent invalidation.
/// </summary>
public interface IConsoleAccountSessionSnapshotStore
{
    /// <summary>Captures the current session and its opaque storage identity.</summary>
    Task<ConsoleAccountSessionSnapshot> CaptureSessionAsync(string profileId, CancellationToken cancellationToken = default);

    /// <summary>Checks that a captured session still belongs to the current profile slot.</summary>
    Task<bool> IsCurrentSessionAsync(ConsoleAccountSessionSnapshot snapshot, CancellationToken cancellationToken = default);

    /// <summary>Clears only while the captured profile slot remains current.</summary>
    Task<bool> TryClearSessionAsync(ConsoleAccountSessionSnapshot snapshot, CancellationToken cancellationToken = default);

    /// <summary>Writes only while the captured session and storage identity remain current.</summary>
    Task<bool> TrySaveSessionAsync(ConsoleAccountSessionSnapshot snapshot, ConsoleAccountSession session,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// An account-session snapshot whose profile slot identity remains private to storage.
/// </summary>
public sealed class ConsoleAccountSessionSnapshot
{
    internal ConsoleAccountSessionSnapshot(string profileId, ConsoleAccountSession? session, object owner)
    {
        ProfileId = profileId;
        Session = session;
        Owner = owner;
    }

    /// <summary>The session observed when the snapshot was captured, or null when absent.</summary>
    public ConsoleAccountSession? Session { get; }

    internal string ProfileId { get; }
    internal object Owner { get; }
}
