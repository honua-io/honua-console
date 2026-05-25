using System.Net;
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
        Assert.False(outcome.UsesMutualTls);

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
        Assert.True(outcome.UsesMutualTls);

        var state = await harness.Store.GetStateAsync("prod-west");
        Assert.Equal(NativeServerTrust.ComputeSha256Thumbprint(certificate), state?.PinnedClientCertificateThumbprint);
    }

    [Fact]
    public async Task Connect_NonMtlsHttpsProfile_WhenServerFingerprintCannotBeObserved_ReportsUnreachable()
    {
        var harness = new Harness();
        harness.Probe.Fingerprint = null;
        await harness.Store.UpsertProfileAsync(NonMtlsProfile());

        var outcome = await harness.Manager.ConnectAsync("dev-east");

        Assert.Equal(ConsoleConnectionStatus.Unreachable, outcome.Status);
        Assert.False(harness.Manager.IsConnected("dev-east"));
        var state = await harness.Store.GetStateAsync("dev-east");
        Assert.Null(state);
    }

    [Fact]
    public async Task Connect_WhenServerFingerprintCannotBeObserved_PreservesExistingServerCertificateChangeBlock()
    {
        var harness = new Harness();
        harness.Probe.Fingerprint = null;
        var existingConnectedAt = DateTimeOffset.Parse("2026-05-24T12:00:00Z");
        var existingTrust = new HonuaEnvironmentTrustState
        {
            Status = HonuaCertificateValidationStatus.Untrusted,
            CheckedAt = DateTimeOffset.Parse("2026-05-24T11:59:00Z"),
            ServerFingerprint = "SERVER-AAA",
            ReasonCode = ConsoleTrustReasonCodes.ServerCertificateChanged,
            SanitizedMessage = "Existing server certificate change block."
        };
        await harness.Store.UpsertProfileAsync(NonMtlsProfile());
        await harness.Store.SaveStateAsync(new ConsoleEnvironmentState
        {
            ProfileId = "dev-east",
            LastRoute = "/operate/native-stream",
            LastStreamingResumeToken = "resume-existing",
            LastConnectedAt = existingConnectedAt,
            PinnedServerFingerprint = "SERVER-AAA",
            PinnedClientCertificateThumbprint = "CLIENT-AAA",
            TrustBlocked = true,
            Trust = existingTrust,
            Diagnostics = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["stream"] = "existing"
            }
        });

        var outcome = await harness.Manager.ConnectAsync("dev-east");

        Assert.Equal(ConsoleConnectionStatus.Unreachable, outcome.Status);
        Assert.False(harness.Manager.IsConnected("dev-east"));
        var state = await harness.Store.GetStateAsync("dev-east");
        Assert.NotNull(state);
        Assert.Equal("/operate/native-stream", state.LastRoute);
        Assert.Equal("resume-existing", state.LastStreamingResumeToken);
        Assert.Equal(existingConnectedAt, state.LastConnectedAt);
        Assert.Equal("SERVER-AAA", state.PinnedServerFingerprint);
        Assert.Equal("CLIENT-AAA", state.PinnedClientCertificateThumbprint);
        Assert.True(state.TrustBlocked);
        Assert.Equal(existingTrust, state.Trust);
        Assert.Equal("existing", state.Diagnostics["stream"]);
    }

    [Fact]
    public async Task Acknowledge_WhenServerFingerprintCannotBeObserved_PreservesExistingServerCertificateChangeBlock()
    {
        var harness = new Harness();
        harness.Probe.Fingerprint = null;
        var existingConnectedAt = DateTimeOffset.Parse("2026-05-24T12:00:00Z");
        var existingTrust = new HonuaEnvironmentTrustState
        {
            Status = HonuaCertificateValidationStatus.Untrusted,
            CheckedAt = DateTimeOffset.Parse("2026-05-24T11:59:00Z"),
            ServerFingerprint = "SERVER-AAA",
            ReasonCode = ConsoleTrustReasonCodes.ServerCertificateChanged,
            SanitizedMessage = "Existing server certificate change block."
        };
        await harness.Store.UpsertProfileAsync(NonMtlsProfile());
        await harness.Store.SaveStateAsync(new ConsoleEnvironmentState
        {
            ProfileId = "dev-east",
            LastRoute = "/environments/dev-east",
            LastConnectedAt = existingConnectedAt,
            PinnedServerFingerprint = "SERVER-AAA",
            TrustBlocked = true,
            Trust = existingTrust
        });

        var outcome = await harness.Manager.AcknowledgeServerCertificateAsync("dev-east");

        Assert.Equal(ConsoleConnectionStatus.Unreachable, outcome.Status);
        var state = await harness.Store.GetStateAsync("dev-east");
        Assert.NotNull(state);
        Assert.Equal("/environments/dev-east", state.LastRoute);
        Assert.Equal(existingConnectedAt, state.LastConnectedAt);
        Assert.Equal("SERVER-AAA", state.PinnedServerFingerprint);
        Assert.True(state.TrustBlocked);
        Assert.Equal(existingTrust, state.Trust);
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
    public async Task Connect_MtlsProfileWithBadCertificateFileReference_PersistsMissingTrustState()
    {
        var store = new JsonConsoleEnvironmentProfileStore(new InMemoryConsoleProfileStorage());
        var profile = MtlsProfile() with
        {
            ClientCertificate = new ConsoleClientCertificateBinding
            {
                Enabled = true,
                Reference = new ConsoleClientCertificateReference
                {
                    Kind = ConsoleClientCertificateReferenceKind.FilePath,
                    Value = Path.Combine(Path.GetTempPath(), $"honua-console-missing-client-cert-{Guid.NewGuid():N}.pfx")
                }
            }
        };
        await store.UpsertProfileAsync(profile);

        var resolver = new StoreClientCertificateResolver(new InMemoryNativeSecretStore());
        var manager = new ConsoleConnectionManager(
            store,
            resolver,
            new FakeServerProbe { Fingerprint = "SERVER-AAA" },
            new FakeValidationClient(),
            new ConsoleTrustEvaluator(),
            new NativeHonuaConnectionFactory(new NullTokenProvider(), resolver));

        var outcome = await manager.ConnectAsync("prod-west");

        Assert.Equal(ConsoleConnectionStatus.Blocked, outcome.Status);
        Assert.Equal(HonuaCertificateValidationStatus.Missing, outcome.Trust.Status);
        var state = await store.GetStateAsync("prod-west");
        Assert.True(state!.TrustBlocked);
        Assert.Equal(ConsoleCertificateValidationCodes.Missing, state.Trust?.ReasonCode);
    }

    [Fact]
    public async Task Connect_MtlsProfile_WhenServerFingerprintCannotBeObserved_DoesNotPinUnvalidatedClientCertificate()
    {
        using var certificate = CreateCertificate();
        var harness = new Harness(certificate);
        harness.Probe.Fingerprint = null;
        var currentThumbprint = NativeServerTrust.ComputeSha256Thumbprint(certificate);
        var existingTrust = new HonuaEnvironmentTrustState
        {
            Status = HonuaCertificateValidationStatus.Ready,
            CheckedAt = DateTimeOffset.Parse("2026-05-24T11:59:00Z"),
            SanitizedMessage = "Existing trust state."
        };
        await harness.Store.UpsertProfileAsync(MtlsProfile());
        await harness.Store.SaveStateAsync(new ConsoleEnvironmentState
        {
            ProfileId = "prod-west",
            LastRoute = "/operate/native-stream",
            Trust = existingTrust
        });

        var outcome = await harness.Manager.ConnectAsync("prod-west");

        Assert.Equal(ConsoleConnectionStatus.Unreachable, outcome.Status);
        Assert.False(harness.Manager.IsConnected("prod-west"));
        var state = await harness.Store.GetStateAsync("prod-west");
        Assert.NotNull(state);
        Assert.Equal(string.Empty, state.PinnedClientCertificateThumbprint);
        Assert.NotEqual(currentThumbprint, state.PinnedClientCertificateThumbprint);
        Assert.Equal(existingTrust, state.Trust);
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
        var currentThumbprint = NativeServerTrust.ComputeSha256Thumbprint(certificate);
        var existingConnectedAt = DateTimeOffset.Parse("2026-05-24T12:00:00Z");
        var existingTrust = new HonuaEnvironmentTrustState
        {
            Status = HonuaCertificateValidationStatus.Ready,
            CheckedAt = DateTimeOffset.Parse("2026-05-24T11:59:00Z"),
            ServerFingerprint = "SERVER-AAA",
            IssuerSummary = "CN=Existing",
            ReasonCode = "existing_trust",
            SanitizedMessage = "Existing trust state."
        };
        await harness.Store.UpsertProfileAsync(MtlsProfile());
        await harness.Store.SaveStateAsync(new ConsoleEnvironmentState
        {
            ProfileId = "prod-west",
            LastRoute = "/operate/native-stream",
            LastStreamingResumeToken = "resume-existing",
            LastConnectedAt = existingConnectedAt,
            PinnedServerFingerprint = "SERVER-AAA",
            Trust = existingTrust
        });

        var outcome = await harness.Manager.ConnectAsync("prod-west");

        Assert.Equal(ConsoleConnectionStatus.Unreachable, outcome.Status);
        Assert.False(harness.Manager.IsConnected("prod-west"));
        var state = await harness.Store.GetStateAsync("prod-west");
        Assert.NotNull(state);
        Assert.Equal(existingConnectedAt, state.LastConnectedAt);
        Assert.Equal("SERVER-AAA", state.PinnedServerFingerprint);
        Assert.Equal(string.Empty, state.PinnedClientCertificateThumbprint);
        Assert.NotEqual(currentThumbprint, state.PinnedClientCertificateThumbprint);
        Assert.Equal(existingTrust, state.Trust);
    }

    [Fact]
    public async Task Connect_ResolvesClientCertificateOnce_AndAttachesTheValidatedCertificate()
    {
        using var certificate = CreateCertificate();
        var harness = new Harness(certificate);
        harness.Probe.Fingerprint = "SERVER-AAA";
        harness.Validation.Result = ReadyResult();
        await harness.Store.UpsertProfileAsync(MtlsProfile());

        var outcome = await harness.Manager.ConnectAsync("prod-west");

        Assert.Equal(ConsoleConnectionStatus.Connected, outcome.Status);

        // The bound certificate is resolved exactly once, so the transport attaches the same
        // instance the trust gate validated (no resolve-time-of-check/use gap).
        Assert.Equal(1, harness.Resolver.ResolveCount);
        Assert.Equal(1, harness.Validation.CallCount);

        var state = await harness.Store.GetStateAsync("prod-west");
        Assert.Equal(harness.Validation.LastValidatedThumbprint, state!.PinnedClientCertificateThumbprint);
        Assert.Equal(NativeServerTrust.ComputeSha256Thumbprint(certificate), state.PinnedClientCertificateThumbprint);
    }

    [Fact]
    public async Task Acknowledge_WhenStillBlockedByClientCertificate_DropsLiveConnection()
    {
        using var certificate = CreateCertificate();
        var harness = new Harness(certificate);
        harness.Probe.Fingerprint = "SERVER-AAA";
        harness.Validation.Result = ReadyResult();
        await harness.Store.UpsertProfileAsync(MtlsProfile());

        var connected = await harness.Manager.ConnectAsync("prod-west");
        Assert.Equal(ConsoleConnectionStatus.Connected, connected.Status);
        Assert.True(harness.Manager.IsConnected("prod-west"));

        // The server now rejects the client certificate. Acknowledging the server identity must not
        // leave the stale live connection in place when the result is still blocked.
        harness.Validation.Result = new ConsoleClientCertificateValidationResult
        {
            Valid = false,
            Code = ConsoleCertificateValidationCodes.Revoked,
            Detail = "Certificate revoked."
        };

        var acknowledged = await harness.Manager.AcknowledgeServerCertificateAsync("prod-west");

        Assert.Equal(ConsoleConnectionStatus.Blocked, acknowledged.Status);
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

    [Fact]
    public async Task Connect_WhenServerBecomesUnreachable_DropsPreviouslyLiveConnection()
    {
        var harness = new Harness();
        await harness.Store.UpsertProfileAsync(NonMtlsProfile());

        harness.Probe.Fingerprint = "SERVER-AAA";
        var connected = await harness.Manager.ConnectAsync("dev-east");
        Assert.Equal(ConsoleConnectionStatus.Connected, connected.Status);
        Assert.True(harness.Manager.IsConnected("dev-east"));

        // The server can no longer be reached on a later connect. Unreachable means "no usable
        // connection", so the stale live connection must be dropped while persisted trust state,
        // pins, and LastConnectedAt are preserved.
        harness.Probe.Fingerprint = null;
        var unreachable = await harness.Manager.ConnectAsync("dev-east");

        Assert.Equal(ConsoleConnectionStatus.Unreachable, unreachable.Status);
        Assert.False(harness.Manager.IsConnected("dev-east"));

        var state = await harness.Store.GetStateAsync("dev-east");
        Assert.Equal("SERVER-AAA", state!.PinnedServerFingerprint);
        Assert.NotNull(state.LastConnectedAt);
        Assert.False(state.TrustBlocked);
    }

    [Fact]
    public async Task Revalidate_WhenServerBecomesUnreachable_DropsLiveConnection()
    {
        var harness = new Harness();
        await harness.Store.UpsertProfileAsync(NonMtlsProfile());

        harness.Probe.Fingerprint = "SERVER-AAA";
        await harness.Manager.ConnectAsync("dev-east");
        Assert.True(harness.Manager.IsConnected("dev-east"));

        harness.Probe.Fingerprint = null;
        var outcome = await harness.Manager.RevalidateAsync("dev-east");

        Assert.Equal(ConsoleConnectionStatus.Unreachable, outcome.Status);
        Assert.False(harness.Manager.IsConnected("dev-east"));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Connect_MtlsProfile_WhenValidationReturnsAuthorizationFailure_BlocksAsInsufficientRbac(HttpStatusCode statusCode)
    {
        using var certificate = CreateCertificate();
        var harness = new Harness(certificate);
        harness.Probe.Fingerprint = "SERVER-AAA";
        harness.Validation.ThrowStatus = statusCode;
        await harness.Store.UpsertProfileAsync(MtlsProfile());

        var outcome = await harness.Manager.ConnectAsync("prod-west");

        // An admin-endpoint authorization failure is a permission/trust problem, not a network
        // failure: it must block (not report Unreachable) so the operator sees a trust diagnostic.
        Assert.Equal(ConsoleConnectionStatus.Blocked, outcome.Status);
        Assert.Equal(HonuaCertificateValidationStatus.Rejected, outcome.Trust.Status);
        Assert.Equal(ConsoleCertificateValidationCodes.InsufficientRbac, outcome.Trust.ReasonCode);
        Assert.False(harness.Manager.IsConnected("prod-west"));

        var state = await harness.Store.GetStateAsync("prod-west");
        Assert.True(state!.TrustBlocked);
        Assert.Equal(ConsoleCertificateValidationCodes.InsufficientRbac, state.Trust?.ReasonCode);
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

        public HttpStatusCode? ThrowStatus { get; set; }

        public int CallCount { get; private set; }

        public string? LastValidatedThumbprint { get; private set; }

        public Task<ConsoleClientCertificateValidationResult> ValidateAsync(
            ConsoleEnvironmentProfile profile,
            X509Certificate2 clientCertificate,
            string? trustedServerFingerprint = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastValidatedThumbprint = NativeServerTrust.ComputeSha256Thumbprint(clientCertificate);

            if (ThrowStatus is { } status)
            {
                // Mirror HttpResponseMessage.EnsureSuccessStatusCode(), which throws with the status.
                throw new HttpRequestException($"Server returned {(int)status}.", inner: null, statusCode: status);
            }

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

        public int ResolveCount { get; private set; }

        public ValueTask<X509Certificate2?> ResolveAsync(
            ConsoleEnvironmentProfile profile,
            CancellationToken cancellationToken = default)
        {
            ResolveCount++;

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
