using System.Runtime.CompilerServices;
using Honua.Console.Native.Core.Connections;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.Native.Core.Streaming;

public sealed class NativeGrpcTelemetryStreamingProof : IConsoleNativeStreamingProof
{
    private readonly NativeHonuaConnectionFactory _connectionFactory;

    public NativeGrpcTelemetryStreamingProof(NativeHonuaConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
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

        await using var connection = await _connectionFactory.CreateAsync(profile, cancellationToken)
            .ConfigureAwait(false);

        foreach (var streamEvent in CreateFixtureEvents(profile))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return streamEvent with
            {
                Transport = connection.ClientCertificate is null
                    ? "grpc/native"
                    : "grpc/native+mtls"
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
