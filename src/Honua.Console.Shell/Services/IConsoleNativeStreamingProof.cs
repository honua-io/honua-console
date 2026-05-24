using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public interface IConsoleNativeStreamingProof
{
    string ProofName { get; }

    IAsyncEnumerable<ConsoleStreamingEvent> StreamAsync(
        ConsoleEnvironmentProfile profile,
        CancellationToken cancellationToken = default);
}

public sealed record ConsoleStreamingEvent(
    string EnvironmentProfileId,
    string Transport,
    string EventKind,
    string Message,
    double? Value,
    string ResumeToken,
    DateTimeOffset Timestamp);
