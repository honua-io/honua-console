using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Honua.Console.Native.Core.Connections;
using Honua.Console.Native.Core.Security;
using Honua.Console.Native.Core.Storage;
using Honua.Console.Native.Core.Streaming;
using Honua.Console.Shell.Models;

namespace Honua.Console.Native.Core.Tests;

public sealed class NativeGrpcTelemetryStreamingProofTests
{
    [Fact]
    public async Task StreamAsyncReturnsNativeGrpcTelemetryEventsForActiveEnvironment()
    {
        var profile = ConsoleEnvironmentProfileDefaults.CreateProfiles()
            .First(profile => profile.Id == ConsoleEnvironmentProfileDefaults.DevelopmentProfileId);
        var sessions = new JsonConsoleAccountSessionStore(new InMemoryNativeSecretStore());
        await sessions.SaveSessionAsync(new ConsoleAccountSession
        {
            ProfileId = profile.Id,
            AccountId = profile.Account.AccountId,
            TenantId = profile.TenantId,
            AccessToken = "dev-token"
        });
        var proof = new NativeGrpcTelemetryStreamingProof(
            new NativeHonuaConnectionFactory(
                new NativeSecretStoreAccountTokenProvider(sessions),
                new NullCertificateResolver()));

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
    }

    [Fact]
    public async Task StreamAsyncMarksTransportWhenMtlsCertificateIsAttached()
    {
        using var certificate = CreateCertificate();
        var profile = ConsoleEnvironmentProfileDefaults.CreateProfiles()
            .First(profile => profile.Id == ConsoleEnvironmentProfileDefaults.StagingProfileId);
        var proof = new NativeGrpcTelemetryStreamingProof(
            new NativeHonuaConnectionFactory(
                new StaticTokenProvider("staging-token"),
                new StaticCertificateResolver(certificate)));

        var events = new List<Honua.Console.Shell.Services.ConsoleStreamingEvent>();
        await foreach (var item in proof.StreamAsync(profile))
        {
            events.Add(item);
        }

        Assert.NotEmpty(events);
        Assert.All(events, item => Assert.Equal("grpc/native+mtls", item.Transport));
        Assert.Equal("staging-resume-3", events[^1].ResumeToken);
    }

    private sealed class StaticTokenProvider : IConsoleAccountTokenProvider
    {
        private readonly string _token;

        public StaticTokenProvider(string token)
        {
            _token = token;
        }

        public ValueTask<string?> GetAccessTokenAsync(
            ConsoleEnvironmentProfile profile,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<string?>(_token);
        }
    }

    private sealed class NullCertificateResolver : IClientCertificateResolver
    {
        public ValueTask<X509Certificate2?> ResolveAsync(
            ConsoleEnvironmentProfile profile,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<X509Certificate2?>(null);
        }
    }

    private sealed class StaticCertificateResolver : IClientCertificateResolver
    {
        private readonly X509Certificate2 _certificate;

        public StaticCertificateResolver(X509Certificate2 certificate)
        {
            _certificate = certificate;
        }

        public ValueTask<X509Certificate2?> ResolveAsync(
            ConsoleEnvironmentProfile profile,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<X509Certificate2?>(_certificate);
        }
    }

    private static X509Certificate2 CreateCertificate()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=Honua Stream Operator", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
    }
}
