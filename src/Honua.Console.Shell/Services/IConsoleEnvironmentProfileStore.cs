using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public interface IConsoleEnvironmentProfileStore
{
    Task<IReadOnlyList<ConsoleEnvironmentProfile>> ListProfilesAsync(CancellationToken cancellationToken = default);

    Task<ConsoleEnvironmentProfile?> GetProfileAsync(string profileId, CancellationToken cancellationToken = default);

    Task<ConsoleEnvironmentProfile?> GetActiveProfileAsync(CancellationToken cancellationToken = default);

    Task UpsertProfileAsync(ConsoleEnvironmentProfile profile, CancellationToken cancellationToken = default);

    Task ActivateProfileAsync(string profileId, CancellationToken cancellationToken = default);

    Task<ConsoleEnvironmentState?> GetStateAsync(string profileId, CancellationToken cancellationToken = default);

    Task SaveStateAsync(ConsoleEnvironmentState state, CancellationToken cancellationToken = default);
}
