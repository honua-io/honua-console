using System.Collections.Concurrent;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.Web.Auth;

/// <summary>
/// Operator-partitioned <see cref="IConsoleEnvironmentProfileStore"/> for the multi-operator browser
/// host (honua-console#233 S1 fix). The previous singleton
/// <see cref="InMemoryConsoleEnvironmentProfileStore"/> held ONE active-profile pointer and ONE set of
/// profiles whose <c>Account</c> binding was rewritten in place to the signing-in operator's identity
/// (see <see cref="ConsoleOperatorSessionBridge"/>). On a host serving multiple operators that made the
/// active profile + its bound operator identity process-wide shared state, so operator A's active
/// profile/identity bled into operator B's requests (which then forwarded A's bearer).
///
/// This decorator keeps the singleton registration (the singleton honua-server clients resolve it at
/// construction) but routes every operation to a per-operator backing store selected by
/// <see cref="IConsoleOperatorContext.CurrentOperatorKey"/>. Each operator therefore has its own active
/// profile selection and its own account binding; no operator can observe another's active profile or
/// identity. The native single-operator host keeps its persistent
/// <see cref="Honua.Console.Native.Core.Storage.JsonConsoleEnvironmentProfileStore"/> singleton
/// unchanged.
///
/// New per-operator partitions are seeded from the configured browser-host server URL (when present) via
/// <paramref name="seedFactory"/>, so each operator still binds to the same configured honua-server on
/// first use without sharing mutable identity state.
/// </summary>
public sealed class OperatorScopedEnvironmentProfileStore : IConsoleEnvironmentProfileStore
{
    private readonly IConsoleOperatorContext _operatorContext;
    private readonly Func<InMemoryConsoleEnvironmentProfileStore> _seedFactory;
    private readonly ConcurrentDictionary<string, InMemoryConsoleEnvironmentProfileStore> _byOperator =
        new(StringComparer.Ordinal);

    public OperatorScopedEnvironmentProfileStore(
        IConsoleOperatorContext operatorContext,
        Func<InMemoryConsoleEnvironmentProfileStore>? seedFactory = null)
    {
        _operatorContext = operatorContext ?? throw new ArgumentNullException(nameof(operatorContext));
        _seedFactory = seedFactory ?? (() => new InMemoryConsoleEnvironmentProfileStore([]));
    }

    // Reads use CurrentOperatorKey (an anonymous public surface legitimately sees an empty profile set).
    // Writes mutate operator-owned profile/identity/state and must NEVER land in the shared anonymous
    // partition — an anonymous surface never writes a profile, so an unresolved operator on a write is a
    // fail-closed bug, not a silent shared-partition write (honua-console#256).
    private InMemoryConsoleEnvironmentProfileStore Current =>
        _byOperator.GetOrAdd(_operatorContext.CurrentOperatorKey, _ => _seedFactory());

    private InMemoryConsoleEnvironmentProfileStore CurrentForWrite =>
        _byOperator.GetOrAdd(_operatorContext.RequireOperatorKey(), _ => _seedFactory());

    public Task<IReadOnlyList<ConsoleEnvironmentProfile>> ListProfilesAsync(CancellationToken cancellationToken = default) =>
        Current.ListProfilesAsync(cancellationToken);

    public Task<ConsoleEnvironmentProfile?> GetProfileAsync(string profileId, CancellationToken cancellationToken = default) =>
        Current.GetProfileAsync(profileId, cancellationToken);

    public Task<ConsoleEnvironmentProfile?> GetActiveProfileAsync(CancellationToken cancellationToken = default) =>
        Current.GetActiveProfileAsync(cancellationToken);

    public Task UpsertProfileAsync(ConsoleEnvironmentProfile profile, CancellationToken cancellationToken = default) =>
        CurrentForWrite.UpsertProfileAsync(profile, cancellationToken);

    public Task ActivateProfileAsync(string profileId, CancellationToken cancellationToken = default) =>
        CurrentForWrite.ActivateProfileAsync(profileId, cancellationToken);

    public Task<ConsoleEnvironmentState?> GetStateAsync(string profileId, CancellationToken cancellationToken = default) =>
        Current.GetStateAsync(profileId, cancellationToken);

    public Task SaveStateAsync(ConsoleEnvironmentState state, CancellationToken cancellationToken = default) =>
        CurrentForWrite.SaveStateAsync(state, cancellationToken);
}
