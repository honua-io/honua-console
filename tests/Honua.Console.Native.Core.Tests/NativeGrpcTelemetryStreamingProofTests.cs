using Honua.Console.Native.Core.Streaming;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.Native.Core.Tests;

public sealed class NativeGrpcTelemetryStreamingProofTests
{
    [Fact]
    public async Task StreamAsyncReturnsNativeGrpcTelemetryEventsForActiveEnvironment()
    {
        var profile = ConsoleEnvironmentProfileDefaults.CreateProfiles()
            .First(profile => profile.Id == ConsoleEnvironmentProfileDefaults.DevelopmentProfileId);
        var connections = new FakeConnectionManager(
            new ConsoleConnectionOutcome { Status = ConsoleConnectionStatus.Connected });
        var proof = new NativeGrpcTelemetryStreamingProof(connections);

        var events = new List<Honua.Console.Shell.Services.ConsoleStreamingEvent>();
        await foreach (var item in proof.StreamAsync(profile))
        {
            events.Add(item);
        }

        Assert.Equal(3, events.Count);
        Assert.All(events, item => Assert.Equal(profile.Id, item.EnvironmentProfileId));
        Assert.All(events, item => Assert.Equal("grpc/native", item.Transport));
        Assert.Contains(events, item => item.EventKind == "jobs.progress");
        Assert.Equal("dev-resume-3", events[^1].ResumeToken);
        Assert.Collection(connections.ConnectAttempts, id => Assert.Equal(profile.Id, id));
    }

    [Fact]
    public async Task StreamAsyncMarksTransportWhenMtlsCertificateIsAttached()
    {
        var profile = ConsoleEnvironmentProfileDefaults.CreateProfiles()
            .First(profile => profile.Id == ConsoleEnvironmentProfileDefaults.StagingProfileId);
        var proof = new NativeGrpcTelemetryStreamingProof(
            new FakeConnectionManager(new ConsoleConnectionOutcome
            {
                Status = ConsoleConnectionStatus.Connected,
                UsesMutualTls = true
            }));

        var events = new List<Honua.Console.Shell.Services.ConsoleStreamingEvent>();
        await foreach (var item in proof.StreamAsync(profile))
        {
            events.Add(item);
        }

        Assert.NotEmpty(events);
        Assert.All(events, item => Assert.Equal("grpc/native+mtls", item.Transport));
        Assert.Equal("staging-resume-3", events[^1].ResumeToken);
    }

    [Fact]
    public async Task StreamAsyncReturnsNoEventsWhenTrustGateBlocks()
    {
        var profile = ConsoleEnvironmentProfileDefaults.CreateProfiles()
            .First(profile => profile.Id == ConsoleEnvironmentProfileDefaults.StagingProfileId);
        var connections = new FakeConnectionManager(new ConsoleConnectionOutcome
        {
            Status = ConsoleConnectionStatus.Blocked,
            Message = "Trust blocked."
        });
        var proof = new NativeGrpcTelemetryStreamingProof(connections);

        var events = new List<Honua.Console.Shell.Services.ConsoleStreamingEvent>();
        await foreach (var item in proof.StreamAsync(profile))
        {
            events.Add(item);
        }

        Assert.Empty(events);
        Assert.Collection(connections.ConnectAttempts, id => Assert.Equal(profile.Id, id));
    }

    [Fact]
    public async Task StreamAsyncDoesNotConnectWhenNativeGrpcIsDisabled()
    {
        var profile = ConsoleEnvironmentProfileDefaults.CreateProfiles()
            .First(profile => profile.Id == ConsoleEnvironmentProfileDefaults.DevelopmentProfileId)
            with
        {
            TransportCapabilities = new ConsoleEnvironmentTransportCapabilities { NativeGrpc = false }
        };
        var connections = new FakeConnectionManager(
            new ConsoleConnectionOutcome { Status = ConsoleConnectionStatus.Connected });
        var proof = new NativeGrpcTelemetryStreamingProof(connections);

        var events = new List<Honua.Console.Shell.Services.ConsoleStreamingEvent>();
        await foreach (var item in proof.StreamAsync(profile))
        {
            events.Add(item);
        }

        Assert.Empty(events);
        Assert.Empty(connections.ConnectAttempts);
    }

    private sealed class FakeConnectionManager : IConsoleConnectionManager
    {
        private readonly ConsoleConnectionOutcome _outcome;

        public FakeConnectionManager(ConsoleConnectionOutcome outcome) => _outcome = outcome;

        public List<string> ConnectAttempts { get; } = [];

        public bool IsConnected(string profileId) => _outcome.IsConnected && ConnectAttempts.Contains(profileId);

        public Task<ConsoleConnectionOutcome> ConnectAsync(
            string profileId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ConnectAttempts.Add(profileId);
            return Task.FromResult(_outcome);
        }

        public Task DisconnectAsync(string profileId, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<ConsoleConnectionOutcome> RevalidateAsync(
            string profileId,
            CancellationToken cancellationToken = default) => Task.FromResult(_outcome);

        public Task<ConsoleConnectionOutcome> AcknowledgeServerCertificateAsync(
            string profileId,
            CancellationToken cancellationToken = default) => Task.FromResult(_outcome);
    }
}
