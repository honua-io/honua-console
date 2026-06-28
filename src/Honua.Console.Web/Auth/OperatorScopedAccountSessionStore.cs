using System.Collections.Concurrent;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.Web.Auth;

/// <summary>
/// Operator-partitioned <see cref="IConsoleAccountSessionStore"/> for the multi-operator browser host
/// (honua-console#233 S1 fix). The previous singleton <see cref="InMemoryConsoleAccountSessionStore"/>
/// held ONE session dictionary keyed only by profile id, so on a host serving multiple operators one
/// operator's forwarded bearer (written keyed by the shared active profile id) was read back for every
/// other operator — a cross-operator bearer/identity bleed.
///
/// This decorator keeps the singleton registration (the singleton honua-server clients resolve it at
/// construction) but routes every operation to a per-operator backing store selected by
/// <see cref="IConsoleOperatorContext.CurrentOperatorKey"/>, so an operator can only ever read its own
/// session. The native single-operator host keeps its persistent
/// <see cref="Honua.Console.Native.Core.Storage.JsonConsoleAccountSessionStore"/> singleton unchanged.
/// </summary>
public sealed class OperatorScopedAccountSessionStore : IConsoleAccountSessionStore
{
    private readonly IConsoleOperatorContext _operatorContext;
    private readonly ConcurrentDictionary<string, InMemoryConsoleAccountSessionStore> _byOperator =
        new(StringComparer.Ordinal);

    public OperatorScopedAccountSessionStore(IConsoleOperatorContext operatorContext)
    {
        _operatorContext = operatorContext ?? throw new ArgumentNullException(nameof(operatorContext));
    }

    // Reads use CurrentOperatorKey (the shared anonymous partition is legitimately empty of operator
    // sessions, so an anonymous read simply finds nothing). Writes carry operator-scoped credentials and
    // must NEVER land in the shared anonymous partition — an anonymous surface never writes operator
    // sessions, so an unresolved operator on a write is a fail-closed bug, not a no-op (honua-console#256).
    private InMemoryConsoleAccountSessionStore Current =>
        _byOperator.GetOrAdd(_operatorContext.CurrentOperatorKey, _ => new InMemoryConsoleAccountSessionStore());

    private InMemoryConsoleAccountSessionStore CurrentForWrite =>
        _byOperator.GetOrAdd(_operatorContext.RequireOperatorKey(), _ => new InMemoryConsoleAccountSessionStore());

    public Task<ConsoleAccountSession?> GetSessionAsync(string profileId, CancellationToken cancellationToken = default) =>
        Current.GetSessionAsync(profileId, cancellationToken);

    public Task SaveSessionAsync(ConsoleAccountSession session, CancellationToken cancellationToken = default) =>
        CurrentForWrite.SaveSessionAsync(session, cancellationToken);

    public Task ClearSessionAsync(string profileId, CancellationToken cancellationToken = default) =>
        CurrentForWrite.ClearSessionAsync(profileId, cancellationToken);
}
