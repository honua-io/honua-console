using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public sealed class InMemoryConsoleEnvironmentProfileStore : IConsoleEnvironmentProfileStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, ConsoleEnvironmentProfile> _profiles;
    private readonly Dictionary<string, ConsoleEnvironmentState> _states;
    private string? _activeProfileId;

    public InMemoryConsoleEnvironmentProfileStore(
        IEnumerable<ConsoleEnvironmentProfile> profiles,
        IEnumerable<ConsoleEnvironmentState>? states = null,
        string? activeProfileId = null)
    {
        ArgumentNullException.ThrowIfNull(profiles);

        _profiles = profiles.ToDictionary(profile => profile.Id, StringComparer.Ordinal);
        _states = (states ?? [])
            .ToDictionary(state => state.ProfileId, StringComparer.Ordinal);
        _activeProfileId = activeProfileId ?? _profiles.Keys.FirstOrDefault();
    }

    public static InMemoryConsoleEnvironmentProfileStore CreateSeeded() =>
        new(
            ConsoleEnvironmentProfileDefaults.CreateProfiles(),
            ConsoleEnvironmentProfileDefaults.CreateStates(),
            ConsoleEnvironmentProfileDefaults.DevelopmentProfileId);

    public Task<IReadOnlyList<ConsoleEnvironmentProfile>> ListProfilesAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<ConsoleEnvironmentProfile>>(
                _profiles.Values.OrderBy(profile => profile.DisplayName, StringComparer.Ordinal).ToArray());
        }
    }

    public Task<ConsoleEnvironmentProfile?> GetProfileAsync(
        string profileId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            _profiles.TryGetValue(profileId, out var profile);
            return Task.FromResult(profile);
        }
    }

    public Task<ConsoleEnvironmentProfile?> GetActiveProfileAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (_activeProfileId is null || !_profiles.TryGetValue(_activeProfileId, out var profile))
            {
                profile = _profiles.Values.FirstOrDefault();
            }

            return Task.FromResult(profile);
        }
    }

    public Task UpsertProfileAsync(ConsoleEnvironmentProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(profile.Id);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            _profiles[profile.Id] = profile with { UpdatedAt = DateTimeOffset.UtcNow };
            _activeProfileId ??= profile.Id;
        }

        return Task.CompletedTask;
    }

    public Task ActivateProfileAsync(string profileId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (!_profiles.ContainsKey(profileId))
            {
                throw new InvalidOperationException($"Environment profile '{profileId}' does not exist.");
            }

            _activeProfileId = profileId;
        }

        return Task.CompletedTask;
    }

    public Task<ConsoleEnvironmentState?> GetStateAsync(string profileId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            _states.TryGetValue(profileId, out var state);
            return Task.FromResult(state);
        }
    }

    public Task SaveStateAsync(ConsoleEnvironmentState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.ProfileId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            _states[state.ProfileId] = state;
        }

        return Task.CompletedTask;
    }
}
