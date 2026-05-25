using System.Runtime.CompilerServices;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.Native.Core.Streaming;

public sealed class NativeGrpcTelemetryStreamingProof : IConsoleNativeStreamingProof
{
    private readonly IConsoleConnectionManager _connections;

    public NativeGrpcTelemetryStreamingProof(IConsoleConnectionManager connections)
    {
        _connections = connections;
    }

    public string ProofName => "Native gRPC telemetry fixture";

    public async IAsyncEnumerable<ConsoleStreamingEvent> StreamAsync(
        ConsoleEnvironmentProfile profile,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (!profile.TransportCapabilities.NativeGrpc)
        {
            yield break;
        }

        var outcome = await _connections.ConnectAsync(profile.Id, cancellationToken).ConfigureAwait(false);
        if (!outcome.IsConnected)
        {
            yield break;
        }

        foreach (var streamEvent in CreateFixtureEvents(profile))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return streamEvent with
            {
                Transport = outcome.UsesMutualTls ? "grpc/native+mtls" : "grpc/native"
            };
        }
    }

    private static IReadOnlyList<ConsoleStreamingEvent> CreateFixtureEvents(ConsoleEnvironmentProfile profile) =>
    [
        new(
            profile.Id,
            "grpc/native",
            "telemetry.subscribed",
            "Telemetry stream opened for active environment.",
            null,
            $"{profile.Id}-resume-1",
            new DateTimeOffset(2026, 5, 23, 18, 0, 0, TimeSpan.Zero)),
        new(
            profile.Id,
            "grpc/native",
            "jobs.progress",
            "Publish job batch progress received.",
            0.42,
            $"{profile.Id}-resume-2",
            new DateTimeOffset(2026, 5, 23, 18, 0, 1, TimeSpan.Zero)),
        new(
            profile.Id,
            "grpc/native",
            "telemetry.sample",
            "Server ingest latency sample received.",
            18,
            $"{profile.Id}-resume-3",
            new DateTimeOffset(2026, 5, 23, 18, 0, 2, TimeSpan.Zero))
    ];
}
