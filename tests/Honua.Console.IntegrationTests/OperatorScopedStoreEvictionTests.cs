using Honua.Console.Shell.Models;
using Honua.Console.Web.Auth;

namespace Honua.Console.IntegrationTests;

public sealed class OperatorScopedStoreEvictionTests
{
    [Fact]
    public async Task EvictOperator_DropsThePartition_SoASubsequentReadStartsFresh()
    {
        var context = new SettableOperatorContext { CurrentOperatorKey = "operator-a" };
        var sessions = new OperatorScopedAccountSessionStore(context);
        var profiles = new OperatorScopedEnvironmentProfileStore(context);

        await profiles.UpsertProfileAsync(new ConsoleEnvironmentProfile
        {
            Id = "env-1",
            DisplayName = "A",
            ServerBaseUri = new Uri("https://a.honua.test"),
            Account = new ConsoleAccountBinding { AuthMode = ConsoleAccountAuthMode.AccountRbac, AccountId = "operator-a" },
        });
        await sessions.SaveSessionAsync(new ConsoleAccountSession
        {
            ProfileId = "env-1",
            AccountId = "operator-a",
            AccessToken = "bearer-A",
        });

        // Sanity: the operator's state is present.
        Assert.NotNull(await sessions.GetSessionAsync("env-1"));
        Assert.Single(await profiles.ListProfilesAsync());

        // Sign-out eviction.
        profiles.EvictOperator("operator-a");
        sessions.EvictOperator("operator-a");

        // The partition is gone: reading again lazily creates a fresh (empty) one.
        Assert.Null(await sessions.GetSessionAsync("env-1"));
        Assert.Empty(await profiles.ListProfilesAsync());
    }

    [Fact]
    public async Task IdlePartition_IsPrunedOnceItExceedsTheIdleLifetime()
    {
        var time = new MutableTimeProvider(DateTimeOffset.UnixEpoch);
        var context = new SettableOperatorContext { CurrentOperatorKey = "operator-idle" };
        var sessions = new OperatorScopedAccountSessionStore(context, time);

        await sessions.SaveSessionAsync(new ConsoleAccountSession
        {
            ProfileId = "env-1",
            AccountId = "operator-idle",
            AccessToken = "bearer-idle",
        });
        Assert.NotNull(await sessions.GetSessionAsync("env-1"));

        // Advance past the idle lifetime, then touch a DIFFERENT operator to trigger pruning.
        time.Advance(OperatorPartitionTable<int>.IdleLifetime + TimeSpan.FromMinutes(1));
        context.CurrentOperatorKey = "operator-fresh";
        _ = await sessions.GetSessionAsync("env-1"); // access as operator-fresh, prunes the idle one

        // The idle operator's partition was pruned: reading it back yields a fresh empty partition.
        context.CurrentOperatorKey = "operator-idle";
        Assert.Null(await sessions.GetSessionAsync("env-1"));
    }

    [Fact]
    public void PartitionTable_CapsAtMaximumOperators_EvictingLeastRecentlyUsed()
    {
        var time = new MutableTimeProvider(DateTimeOffset.UnixEpoch);
        var table = new OperatorPartitionTable<int>(time);

        // Add one more than the cap; each add advances time so last-access order is deterministic.
        for (var i = 0; i <= OperatorPartitionTable<int>.MaximumOperators; i++)
        {
            time.Advance(TimeSpan.FromSeconds(1));
            var captured = i;
            _ = table.GetOrAdd($"operator-{i}", () => captured);
        }

        Assert.True(table.Count <= OperatorPartitionTable<int>.MaximumOperators);
    }

    private sealed class SettableOperatorContext : IConsoleOperatorContext
    {
        public string CurrentOperatorKey { get; set; } = ConsoleOperatorContext.AnonymousKey;

        public bool HasOperator => CurrentOperatorKey != ConsoleOperatorContext.AnonymousKey;

        public string RequireOperatorKey() => HasOperator
            ? CurrentOperatorKey
            : throw new ConsoleOperatorContextUnresolvedException();
    }

    private sealed class MutableTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now = _now.Add(by);
    }
}
