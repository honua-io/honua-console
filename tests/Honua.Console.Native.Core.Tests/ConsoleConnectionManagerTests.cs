using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Honua.Console.Contracts;
using Honua.Console.Native.Core.Connections;
using Honua.Console.Native.Core.Security;
using Honua.Console.Native.Core.Storage;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.Native.Core.Tests;

public sealed class ConsoleConnectionManagerTests
{
    [Fact]
    public async Task Connect_NonMtlsProfile_ConnectsAndPinsServerFingerprint()
    {
        var harness = new Harness();
        harness.Probe.Fingerprint = "SERVER-AAA";
        await harness.Store.UpsertProfileAsync(NonMtlsProfile());

        var outcome = await harness.Manager.ConnectAsync("dev-east");

        Assert.Equal(ConsoleConnectionStatus.Connected, outcome.Status);
        Assert.True(harness.Manager.IsConnected("dev-east"));

        var state = await harness.Store.GetStateAsync("dev-east");
        Assert.Equal("SERVER-AAA", state?.PinnedServerFingerprint);
        Assert.NotNull(state?.LastConnectedAt);
        Assert.False(state!.TrustBlocked);
    }

    [Fact]
    public async Task Connect_MtlsProfileWithValidCertificate_ReportsReady()
    {
        using var certificate = CreateCertificate();
        var harness = new Harness(certificate);
        harness.Probe.Fingerprint = "SERVER-AAA";
        harness.Validation.Result = ReadyResult();
        await harness.Store.UpsertProfileAsync(MtlsProfile());

        var outcome = await harness.Manager.ConnectAsync("prod-west");

        Assert.Equal(ConsoleConnectionStatus.Connected, outcome.Status);
        Assert.Equal(HonuaCertificateValidationStatus.Ready, outcome.Trust.Status);

        var state = await harness.Store.GetStateAsync("prod-west");
        Assert.Equal(NativeServerTrust.ComputeSha256Thumbprint(certificate), state?.PinnedClientCertificateThumbprint);
    }

    [Fact]
    public async Task Connect_AfterServerCertificateChanges_BlocksUntilAcknowledged()
    {
        var harness = new Harness();
        await harness.Store.UpsertProfileAsync(NonMtlsProfile());

        // First connect pins SERVER-AAA.
        harness.Probe.Fingerprint = "SERVER-AAA";
        await harness.Manager.ConnectAsync("dev-east");

        // Server identity changes: connection must be refused.
        harness.Probe.Fingerprint = "SERVER-BBB";
        var blocked = await harness.Manager.ConnectAsync("dev-east");

        Assert.Equal(ConsoleConnectionStatus.Blocked, blocked.Status);
        Assert.False(harness.Manager.IsConnected("dev-east"));
        var blockedState = await harness.Store.GetStateAsync("dev-east");
        Assert.True(blockedState!.TrustBlocked);
        Assert.Equal(ConsoleTrustReasonCodes.ServerCertificateChanged, blockedState.Trust?.ReasonCode);
        Assert.Equal("SERVER-AAA", blockedState.PinnedServerFingerprint);

        // Acknowledge re-pins the new identity and clears the block.
        var acknowledged = await harness.Manager.AcknowledgeServerCertificateAsync("dev-east");
        Assert.NotEqual(ConsoleConnectionStatus.Blocked, acknowledged.Status);
        var ackState = await harness.Store.GetStateAsync("dev-east");
        Assert.False(ackState!.TrustBlocked);
        Assert.Equal("SERVER-BBB", ackState.PinnedServerFingerprint);

        // A subsequent connect now succeeds.
        var reconnected = await harness.Manager.ConnectAsync("dev-east");
        Assert.Equal(ConsoleConnectionStatus.Connected, reconnected.Status);
    }

    [Fact]
    public async Task Connect_MtlsProfileWithUntrustedCertificate_Blocks()
    {
        using var certificate = CreateCertificate();
        var harness = new Harness(certificate);
        harness.Probe.Fingerprint = "SERVER-AAA";
        harness.Validation.Result = new ConsoleClientCertificateValidationResult
        {
            Valid = false,
            Code = ConsoleCertificateValidationCodes.UntrustedIssuer,
            Detail = "Issuer is not trusted."
        };
        await harness.Store.UpsertProfileAsync(MtlsProfile());

        var outcome = await harness.Manager.ConnectAsync("prod-west");

        Assert.Equal(ConsoleConnectionStatus.Blocked, outcome.Status);
        Assert.Equal(HonuaCertificateValidationStatus.Untrusted, outcome.Trust.Status);
        Assert.False(harness.Manager.IsConnected("prod-west"));
    }

    [Fact]
    public async Task Connect_MtlsProfileWithMissingCertificate_Blocks()
    {
        var harness = new Harness();
        harness.Probe.Fingerprint = "SERVER-AAA";
        await harness.Store.UpsertProfileAsync(MtlsProfile());

        var outcome = await harness.Manager.ConnectAsync("prod-west");

        Assert.Equal(ConsoleConnectionStatus.Blocked, outcome.Status);
        Assert.Equal(HonuaCertificateValidationStatus.Missing, outcome.Trust.Status);
        Assert.False(harness.Manager.IsConnected("prod-west"));
    }

    [Fact]
    public async Task Revalidate_AfterClientCertificateChanges_RePinsValidatedCertificate()
    {
        using var original = CreateCertificate("CN=Honua Test Operator Original");
        using var replacement = CreateCertificate("CN=Honua Test Operator Replacement");
        var harness = new Harness(original);
        harness.Probe.Fingerprint = "SERVER-AAA";
        harness.Validation.Result = ReadyResult();
        await harness.Store.UpsertProfileAsync(MtlsProfile());

        await harness.Manager.ConnectAsync("prod-west");
        harness.Resolver.Certificate = replacement;

        var blocked = await harness.Manager.ConnectAsync("prod-west");

        Assert.Equal(ConsoleConnectionStatus.Blocked, blocked.Status);
        Assert.Equal(ConsoleTrustReasonCodes.ClientCertificateChanged, blocked.Trust.ReasonCode);
        Assert.False(harness.Manager.IsConnected("prod-west"));

        var revalidated = await harness.Manager.RevalidateAsync("prod-west");

        Assert.NotEqual(ConsoleConnectionStatus.Blocked, revalidated.Status);
        var state = await harness.Store.GetStateAsync("prod-west");
        Assert.False(state!.TrustBlocked);
        Assert.Equal(NativeServerTrust.ComputeSha256Thumbprint(replacement), state.PinnedClientCertificateThumbprint);
    }

    [Fact]
    public async Task Connect_MtlsProfile_ServerUnreachableForValidation_ReportsUnreachable()
    {
        using var certificate = CreateCertificate();
        var harness = new Harness(certificate);
        harness.Probe.Fingerprint = "SERVER-AAA";
        harness.Validation.ThrowUnreachable = true;
        await harness.Store.UpsertProfileAsync(MtlsProfile());

        var outcome = await harness.Manager.ConnectAsync("prod-west");

        Assert.Equal(ConsoleConnectionStatus.Unreachable, outcome.Status);
        Assert.False(harness.Manager.IsConnected("prod-west"));
    }

    [Fact]
    public async Task Disconnect_ReleasesConnection()
    {
        var harness = new Harness();
        harness.Probe.Fingerprint = "SERVER-AAA";
        await harness.Store.UpsertProfileAsync(NonMtlsProfile());
        await harness.Manager.ConnectAsync("dev-east");
        Assert.True(harness.Manager.IsConnected("dev-east"));

        await harness.Manager.DisconnectAsync("dev-east");

        Assert.False(harness.Manager.IsConnected("dev-east"));
    }

    private static ConsoleEnvironmentProfile NonMtlsProfile() => new()
    {
        Id = "dev-east",
        DisplayName = "Dev East",
        ServerBaseUri = new Uri("https://dev-east.honua.example"),
        EnvironmentKind = "development",
        TenantId = "dev-east",
        TransportCapabilities = new ConsoleEnvironmentTransportCapabilities { NativeGrpc = true }
    };

    private static ConsoleEnvironmentProfile MtlsProfile() => new()
    {
        Id = "prod-west",
        DisplayName = "Prod West",
        ServerBaseUri = new Uri("https://prod-west.honua.example"),
        EnvironmentKind = "production",
        TenantId = "prod-west",
        TransportCapabilities = new ConsoleEnvironmentTransportCapabilities { NativeGrpc = true, NativeMtls = true },
        ClientCertificate = new ConsoleClientCertificateBinding
        {
            Enabled = true,
            Reference = new ConsoleClientCertificateReference
            {
                Kind = ConsoleClientCertificateReferenceKind.StoreThumbprint,
                Value = "THUMB"
            }
        }
    };

    private static ConsoleClientCertificateValidationResult ReadyResult() => new()
    {
        Valid = true,
        Code = ConsoleCertificateValidationCodes.Success,
        Detail = "Trusted.",
        DaysUntilExpiry = 200
    };

    private static X509Certificate2 CreateCertificate(string subject = "CN=Honua Test Operator")
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(subject, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(200));
    }

    private sealed class Harness
    {
        public Harness(X509Certificate2? certificate = null)
        {
            Store = new JsonConsoleEnvironmentProfileStore(new InMemoryConsoleProfileStorage());
            Probe = new FakeServerProbe();
            Validation = new FakeValidationClient();
            var resolver = new StaticCertificateResolver(certificate);
            Resolver = resolver;
            var factory = new NativeHonuaConnectionFactory(new NullTokenProvider(), resolver);
            Manager = new ConsoleConnectionManager(Store, resolver, Probe, Validation, new ConsoleTrustEvaluator(), factory);
        }

        public JsonConsoleEnvironmentProfileStore Store { get; }

        public FakeServerProbe Probe { get; }

        public FakeValidationClient Validation { get; }

        public StaticCertificateResolver Resolver { get; }

        public ConsoleConnectionManager Manager { get; }
    }

    private sealed class FakeServerProbe : IConsoleServerCertificateProbe
    {
        public string? Fingerprint { get; set; }

        public Task<string?> ObserveServerFingerprintAsync(
            ConsoleEnvironmentProfile profile,
            X509Certificate2? clientCertificate,
            CancellationToken cancellationToken = default) => Task.FromResult(Fingerprint);
    }

    private sealed class FakeValidationClient : IConsoleClientCertificateValidationClient
    {
        public ConsoleClientCertificateValidationResult Result { get; set; } = new()
        {
            Valid = true,
            Code = ConsoleCertificateValidationCodes.Success,
            Detail = "Trusted."
        };

        public bool ThrowUnreachable { get; set; }

        public Task<ConsoleClientCertificateValidationResult> ValidateAsync(
            ConsoleEnvironmentProfile profile,
            X509Certificate2 clientCertificate,
            string? trustedServerFingerprint = null,
            CancellationToken cancellationToken = default)
        {
            if (ThrowUnreachable)
            {
                throw new HttpRequestException("Server unreachable.");
            }

            return Task.FromResult(Result);
        }
    }

    private sealed class StaticCertificateResolver : IClientCertificateResolver
    {
        public StaticCertificateResolver(X509Certificate2? certificate) => Certificate = certificate;

        public X509Certificate2? Certificate { get; set; }

        public ValueTask<X509Certificate2?> ResolveAsync(
            ConsoleEnvironmentProfile profile,
            CancellationToken cancellationToken = default)
        {
            // Mirror the real resolver: each call returns a fresh, caller-owned instance the
            // connection manager is free to dispose. (A public-only clone is sufficient for the
            // thumbprint/expiry the trust gate reads; no real TLS handshake runs in this test.)
            if (!profile.ClientCertificate.Enabled || Certificate is null)
            {
                return ValueTask.FromResult<X509Certificate2?>(null);
            }

            return ValueTask.FromResult<X509Certificate2?>(X509CertificateLoader.LoadCertificate(Certificate.RawData));
        }
    }

    private sealed class NullTokenProvider : IConsoleAccountTokenProvider
    {
        public ValueTask<string?> GetAccessTokenAsync(
            ConsoleEnvironmentProfile profile,
            CancellationToken cancellationToken = default) => ValueTask.FromResult<string?>(null);
    }
}
