using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.Web.Auth;

// Routes every call to the current operator's own profile store, selected by
// IConsoleOperatorContext.CurrentOperatorKey, behind one singleton registration that the singleton
// honua-server clients resolve at construction. Each operator has its own active profile and account
// binding. New partitions are seeded from the configured browser-host server URL, so every operator
// binds to the same honua-server without sharing mutable identity state.
public sealed class OperatorScopedEnvironmentProfileStore : IConsoleEnvironmentProfileStore
{
    private readonly IConsoleOperatorContext _operatorContext;
    private readonly Func<InMemoryConsoleEnvironmentProfileStore> _seedFactory;

    // Bounded per-operator partition map. Departed operators are evicted on sign-out via EvictOperator,
    // and idle/over-capacity partitions are pruned, so this no longer grows without bound
    // (honua-console#279 PA-237).
    private readonly OperatorPartitionTable<InMemoryConsoleEnvironmentProfileStore> _byOperator;

    public OperatorScopedEnvironmentProfileStore(
        IConsoleOperatorContext operatorContext,
        Func<InMemoryConsoleEnvironmentProfileStore>? seedFactory = null,
        TimeProvider? timeProvider = null)
    {
        _operatorContext = operatorContext ?? throw new ArgumentNullException(nameof(operatorContext));
        _seedFactory = seedFactory ?? (() => new InMemoryConsoleEnvironmentProfileStore([]));
        _byOperator = new OperatorPartitionTable<InMemoryConsoleEnvironmentProfileStore>(timeProvider);
    }

    // Reads use CurrentOperatorKey (an anonymous public surface legitimately sees an empty profile set).
    // Writes mutate operator-owned profile/identity/state and must NEVER land in the shared anonymous
    // partition — an anonymous surface never writes a profile, so an unresolved operator on a write is a
    // fail-closed bug, not a silent shared-partition write (honua-console#256).
    private InMemoryConsoleEnvironmentProfileStore Current =>
        _byOperator.GetOrAdd(_operatorContext.CurrentOperatorKey, _seedFactory);

    private InMemoryConsoleEnvironmentProfileStore CurrentForWrite =>
        _byOperator.GetOrAdd(_operatorContext.RequireOperatorKey(), _seedFactory);

    /// <summary>
    /// Removes the operator's partition on sign-out so its profiles/active-selection/identity binding do
    /// not linger for the life of the process (honua-console#279 PA-237). No-op if the operator has no
    /// partition.
    /// </summary>
    public void EvictOperator(string operatorKey) => _byOperator.Evict(operatorKey);

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
