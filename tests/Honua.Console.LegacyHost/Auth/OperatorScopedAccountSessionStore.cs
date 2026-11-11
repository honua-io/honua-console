using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.Web.Auth;

// Routes every call to the current operator's own session store, selected by
// IConsoleOperatorContext.CurrentOperatorKey, behind one singleton registration that the singleton
// honua-server clients resolve at construction. An operator can only ever read its own session.
public sealed class OperatorScopedAccountSessionStore : IConsoleAccountSessionStore, IConsoleAccountSessionSnapshotStore
{
    private readonly IConsoleOperatorContext _operatorContext;

    // Bounded per-operator partition map. Departed operators are evicted on sign-out via EvictOperator,
    // and idle/over-capacity partitions are pruned, so this no longer grows without bound
    // (honua-console#279 PA-237).
    private readonly OperatorPartitionTable<InMemoryConsoleAccountSessionStore> _byOperator;

    public OperatorScopedAccountSessionStore(
        IConsoleOperatorContext operatorContext,
        TimeProvider? timeProvider = null)
    {
        _operatorContext = operatorContext ?? throw new ArgumentNullException(nameof(operatorContext));
        _byOperator = new OperatorPartitionTable<InMemoryConsoleAccountSessionStore>(timeProvider);
    }

    // Reads use CurrentOperatorKey (the shared anonymous partition is legitimately empty of operator
    // sessions, so an anonymous read simply finds nothing). Writes carry operator-scoped credentials and
    // must NEVER land in the shared anonymous partition — an anonymous surface never writes operator
    // sessions, so an unresolved operator on a write is a fail-closed bug, not a no-op (honua-console#256).
    private InMemoryConsoleAccountSessionStore Current =>
        _byOperator.GetOrAdd(_operatorContext.CurrentOperatorKey, static () => new InMemoryConsoleAccountSessionStore());

    private InMemoryConsoleAccountSessionStore CurrentForWrite =>
        _byOperator.GetOrAdd(_operatorContext.RequireOperatorKey(), static () => new InMemoryConsoleAccountSessionStore());

    public void EvictOperator(string operatorKey) => _byOperator.Evict(operatorKey);

    public Task<ConsoleAccountSession?> GetSessionAsync(string profileId, CancellationToken cancellationToken = default) =>
        Current.GetSessionAsync(profileId, cancellationToken);

    public Task SaveSessionAsync(ConsoleAccountSession session, CancellationToken cancellationToken = default) =>
        CurrentForWrite.SaveSessionAsync(session, cancellationToken);

    public Task ClearSessionAsync(string profileId, CancellationToken cancellationToken = default) =>
        CurrentForWrite.ClearSessionAsync(profileId, cancellationToken);

    public Task<ConsoleAccountSessionSnapshot> CaptureSessionAsync(
        string profileId, CancellationToken cancellationToken = default) =>
        Current.CaptureSessionAsync(profileId, cancellationToken);

    public Task<bool> IsCurrentSessionAsync(
        ConsoleAccountSessionSnapshot snapshot, CancellationToken cancellationToken = default) =>
        Current.IsCurrentSessionAsync(snapshot, cancellationToken);

    public Task<bool> TryClearSessionAsync(
        ConsoleAccountSessionSnapshot snapshot, CancellationToken cancellationToken = default) =>
        CurrentForWrite.TryClearSessionAsync(snapshot, cancellationToken);

    public Task<bool> TrySaveSessionAsync(
        ConsoleAccountSessionSnapshot snapshot, ConsoleAccountSession session,
        CancellationToken cancellationToken = default) =>
        CurrentForWrite.TrySaveSessionAsync(snapshot, session, cancellationToken);
}
