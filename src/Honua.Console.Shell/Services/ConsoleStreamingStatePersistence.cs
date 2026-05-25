using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public static class ConsoleStreamingStatePersistence
{
    public const string NativeStreamRoute = "/operate/native-stream";

    public static async Task SaveNativeStreamAsync(
        IConsoleEnvironmentProfileStore profiles,
        ConsoleEnvironmentProfile profile,
        string proofName,
        ConsoleStreamingEvent lastEvent,
        TimeProvider? timeProvider = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(lastEvent);

        var existing = await profiles.GetStateAsync(profile.Id, cancellationToken).ConfigureAwait(false)
            ?? new ConsoleEnvironmentState { ProfileId = profile.Id };
        var diagnostics = new Dictionary<string, string>(existing.Diagnostics, StringComparer.Ordinal)
        {
            ["streamProof"] = proofName,
            ["transport"] = lastEvent.Transport
        };

        await profiles.SaveStateAsync(
                existing with
                {
                    ProfileId = profile.Id,
                    LastRoute = NativeStreamRoute,
                    LastConnectedAt = (timeProvider ?? TimeProvider.System).GetUtcNow(),
                    LastStreamingResumeToken = lastEvent.ResumeToken,
                    Diagnostics = diagnostics
                },
                cancellationToken)
            .ConfigureAwait(false);
    }
}
