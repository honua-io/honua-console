using Honua.Console.Native.Core.Storage;
using Honua.Console.Shell.Models;
using Xunit;

namespace Honua.Console.Native.Core.Tests;

/// <summary>
/// The native profile store backs onto platform SecureStorage (keychain/keystore/DPAPI decrypt +
/// IPC) and is resolved per honua-server request by the binding handler. These tests pin that the
/// deserialized snapshot is cached (no decrypt + JSON parse on every read) while staying consistent
/// with writes.
/// </summary>
public sealed class EnvironmentProfileStoreCacheTests
{
    [Fact]
    public async Task RepeatedReadsHitTheBackingStoreOnlyOnce()
    {
        var storage = new CountingProfileStorage();
        var store = new JsonConsoleEnvironmentProfileStore(storage);

        await store.ListProfilesAsync();
        await store.GetActiveProfileAsync();
        await store.ListProfilesAsync();

        Assert.Equal(1, storage.GetCount);
    }

    [Fact]
    public async Task WritesStayConsistentWithTheCache()
    {
        var storage = new CountingProfileStorage();
        var store = new JsonConsoleEnvironmentProfileStore(storage);

        // Prime the cache, then mutate through the same instance.
        await store.ListProfilesAsync();
        await store.UpsertProfileAsync(new ConsoleEnvironmentProfile
        {
            Id = "env-a",
            DisplayName = "env-a",
            ServerBaseUri = new Uri("https://server-a.example"),
            Account = new ConsoleAccountBinding { AuthMode = ConsoleAccountAuthMode.Anonymous, AccountId = "op" }
        });

        var active = await store.GetActiveProfileAsync();
        var profiles = await store.ListProfilesAsync();

        Assert.Equal("env-a", active?.Id);
        Assert.Single(profiles);
        // The write persisted exactly once; reads never re-hit the backing store after priming.
        Assert.Equal(1, storage.GetCount);
        Assert.Equal(1, storage.SetCount);
    }

    private sealed class CountingProfileStorage : IConsoleProfileStorage
    {
        private string? _value;

        public int GetCount { get; private set; }

        public int SetCount { get; private set; }

        public ValueTask<string?> GetAsync(string key, CancellationToken cancellationToken = default)
        {
            GetCount++;
            return ValueTask.FromResult(_value);
        }

        public ValueTask SetAsync(string key, string value, CancellationToken cancellationToken = default)
        {
            SetCount++;
            _value = value;
            return ValueTask.CompletedTask;
        }

        public ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            _value = null;
            return ValueTask.CompletedTask;
        }
    }
}
