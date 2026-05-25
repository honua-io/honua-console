using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.Native.Core.Tests;

public sealed class ConsoleStreamingStatePersistenceTests
{
    [Fact]
    public async Task SaveNativeStreamAsync_PreservesExistingTrustPinsAndDiagnostics()
    {
        var profile = new ConsoleEnvironmentProfile
        {
            Id = "dev-east",
            DisplayName = "Dev East"
        };
        var existingTrust = new HonuaEnvironmentTrustState
        {
            Status = HonuaCertificateValidationStatus.Untrusted,
            ReasonCode = "server_certificate_changed",
            SanitizedMessage = "Certificate changed."
        };
        var store = new InMemoryConsoleEnvironmentProfileStore(
            [profile],
            [
                new ConsoleEnvironmentState
                {
                    ProfileId = profile.Id,
                    LastRoute = "/environments",
                    PinnedServerFingerprint = "SERVER-AAA",
                    PinnedClientCertificateThumbprint = "CLIENT-AAA",
                    TrustBlocked = true,
                    Trust = existingTrust,
                    Diagnostics = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["existing"] = "kept"
                    }
                }
            ],
            profile.Id);
        var lastEvent = new ConsoleStreamingEvent(
            profile.Id,
            "grpc/native",
            "telemetry.sample",
            "Sample.",
            1,
            "dev-resume-3",
            DateTimeOffset.UtcNow);

        await ConsoleStreamingStatePersistence.SaveNativeStreamAsync(
            store,
            profile,
            "Native gRPC telemetry fixture",
            lastEvent);

        var saved = await store.GetStateAsync(profile.Id);
        Assert.NotNull(saved);
        Assert.Equal(ConsoleStreamingStatePersistence.NativeStreamRoute, saved.LastRoute);
        Assert.Equal("dev-resume-3", saved.LastStreamingResumeToken);
        Assert.NotNull(saved.LastConnectedAt);
        Assert.Equal("SERVER-AAA", saved.PinnedServerFingerprint);
        Assert.Equal("CLIENT-AAA", saved.PinnedClientCertificateThumbprint);
        Assert.True(saved.TrustBlocked);
        Assert.Same(existingTrust, saved.Trust);
        Assert.Equal("kept", saved.Diagnostics["existing"]);
        Assert.Equal("Native gRPC telemetry fixture", saved.Diagnostics["streamProof"]);
        Assert.Equal("grpc/native", saved.Diagnostics["transport"]);
    }
}
