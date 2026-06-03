using System.Text.Json;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.Native.Core.Storage;

public sealed class JsonConsoleEnvironmentProfileStore : IConsoleEnvironmentProfileStore
{
    private const string StorageKey = "honua.console.native.environment-profiles.v1";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly IConsoleProfileStorage _storage;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonConsoleEnvironmentProfileStore(IConsoleProfileStorage storage)
    {
        _storage = storage;
    }

    public async Task<IReadOnlyList<ConsoleEnvironmentProfile>> ListProfilesAsync(
        CancellationToken cancellationToken = default)
    {
        var snapshot = await ReadSnapshotAsync(cancellationToken).ConfigureAwait(false);
        return snapshot.Profiles
            .OrderBy(profile => profile.DisplayName, StringComparer.Ordinal)
            .ToArray();
    }

    public async Task<ConsoleEnvironmentProfile?> GetProfileAsync(
        string profileId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        var snapshot = await ReadSnapshotAsync(cancellationToken).ConfigureAwait(false);
        return snapshot.Profiles.FirstOrDefault(profile => string.Equals(profile.Id, profileId, StringComparison.Ordinal));
    }

    public async Task<ConsoleEnvironmentProfile?> GetActiveProfileAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await ReadSnapshotAsync(cancellationToken).ConfigureAwait(false);
        return snapshot.Profiles.FirstOrDefault(profile => string.Equals(profile.Id, snapshot.ActiveProfileId, StringComparison.Ordinal))
            ?? snapshot.Profiles.FirstOrDefault();
    }

    public async Task UpsertProfileAsync(ConsoleEnvironmentProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(profile.Id);

        await UpdateSnapshotAsync(
            snapshot =>
            {
                var index = snapshot.Profiles.FindIndex(item => string.Equals(item.Id, profile.Id, StringComparison.Ordinal));
                var updated = profile with { UpdatedAt = DateTimeOffset.UtcNow };
                if (index >= 0)
                {
                    snapshot.Profiles[index] = updated;
                }
                else
                {
                    snapshot.Profiles.Add(updated);
                }

                snapshot.ActiveProfileId ??= profile.Id;
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task ActivateProfileAsync(string profileId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);

        await UpdateSnapshotAsync(
            snapshot =>
            {
                if (!snapshot.Profiles.Any(profile => string.Equals(profile.Id, profileId, StringComparison.Ordinal)))
                {
                    throw new InvalidOperationException($"Environment profile '{profileId}' does not exist.");
                }

                snapshot.ActiveProfileId = profileId;
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<ConsoleEnvironmentState?> GetStateAsync(
        string profileId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        var snapshot = await ReadSnapshotAsync(cancellationToken).ConfigureAwait(false);
        return snapshot.States.FirstOrDefault(state => string.Equals(state.ProfileId, profileId, StringComparison.Ordinal));
    }

    public async Task SaveStateAsync(ConsoleEnvironmentState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.ProfileId);

        await UpdateSnapshotAsync(
            snapshot =>
            {
                var index = snapshot.States.FindIndex(item => string.Equals(item.ProfileId, state.ProfileId, StringComparison.Ordinal));
                if (index >= 0)
                {
                    snapshot.States[index] = state;
                }
                else
                {
                    snapshot.States.Add(state);
                }
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<EnvironmentProfileSnapshot> ReadSnapshotAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await ReadSnapshotCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task UpdateSnapshotAsync(
        Action<EnvironmentProfileSnapshot> update,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var snapshot = await ReadSnapshotCoreAsync(cancellationToken).ConfigureAwait(false);
            update(snapshot);
            var json = JsonSerializer.Serialize(snapshot, JsonOptions);
            await _storage.SetAsync(StorageKey, json, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<EnvironmentProfileSnapshot> ReadSnapshotCoreAsync(CancellationToken cancellationToken)
    {
        var json = await _storage.GetAsync(StorageKey, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json))
        {
            // First run: start EMPTY, not seeded. Environment profiles are host-owned local state
            // (Console Patterns Charter §11), but they must never ship fabricated demo environments —
            // the operator creates their first environment via /environments/new.
            return new EnvironmentProfileSnapshot();
        }

        return JsonSerializer.Deserialize<EnvironmentProfileSnapshot>(json, JsonOptions)
            ?? new EnvironmentProfileSnapshot();
    }

    private sealed class EnvironmentProfileSnapshot
    {
        public string? ActiveProfileId { get; set; }

        public List<ConsoleEnvironmentProfile> Profiles { get; set; } = [];

        public List<ConsoleEnvironmentState> States { get; set; } = [];
    }
}
