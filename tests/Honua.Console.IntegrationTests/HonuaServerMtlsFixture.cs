using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Honua.Console.Native.Core.Connections;
using Honua.Console.Native.Core.Security;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.IntegrationTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ConsoleTrustIntegrationCollection : ICollectionFixture<HonuaServerMtlsFixture>
{
    public const string Name = "ConsoleTrustIntegration";
}

/// <summary>
/// Boots a real honua-server (with PostgreSQL) configured for mTLS via Testcontainers, or targets an
/// external server, so the trust gate can be asserted against live client-certificate validation
/// (honua-server#1171). Off by default; skips gracefully when Docker, the image, or the opt-in flag
/// is unavailable (AC#4, Console Patterns Charter section 11). Local profile/session storage stays
/// in-memory in the test per the charter section 11 carve-out.
/// </summary>
public sealed class HonuaServerMtlsFixture : IAsyncLifetime, IDisposable
{
    private HonuaServerTestcontainer? _container;

    public ConsoleTrustIntegrationOptions Options { get; } = ConsoleTrustIntegrationOptions.Load();

    public string? SkipReason { get; private set; } = ConsoleTrustIntegrationOptions.GetSkipReason();

    public Uri BaseAddress { get; private set; } = new("https://localhost");

    /// <summary>A self-signed certificate the server does not trust (untrusted issuer).</summary>
    public X509Certificate2 UntrustedClientCertificate { get; } = CreateSelfSignedCertificate("CN=Honua Console Untrusted");

    public async Task InitializeAsync()
    {
        if (SkipReason is not null)
        {
            return;
        }

        if (Options.ExternalBaseUri is not null)
        {
            BaseAddress = Options.ExternalBaseUri;
            return;
        }

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            _container = await HonuaServerTestcontainer.StartAsync(Options, timeout.Token).ConfigureAwait(false);
            BaseAddress = _container.BaseAddress;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The lane is opt-in and must skip - never fail - when Docker/Testcontainers cannot start
            // the server (daemon unavailable, image not pullable, or readiness timeout). Record the
            // reason so the SkippableFact bodies skip with it, and tear down any partially started
            // containers. This keeps the documented "skips gracefully when Docker/the image is
            // unavailable" contract (Console Patterns Charter section 11).
            SkipReason = "The honua-server integration container could not start "
                + $"({ex.GetType().Name}: {ex.Message}). Ensure Docker is running and the configured "
                + "server image is pullable, or set HONUA_CONSOLE_EXTERNAL_BASE_URL.";
            await DisposeStartedContainersAsync().ConfigureAwait(false);
        }
    }

    public Task DisposeAsync() => DisposeStartedContainersAsync();

    private async Task DisposeStartedContainersAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync().ConfigureAwait(false);
            _container = null;
        }
    }

    public void Dispose() => UntrustedClientCertificate.Dispose();

    public ConsoleEnvironmentProfile BuildProfile(string id = "integration") => new()
    {
        Id = id,
        DisplayName = "Integration",
        ServerBaseUri = BaseAddress,
        EnvironmentKind = "integration",
        TenantId = "integration",
        TransportCapabilities = new ConsoleEnvironmentTransportCapabilities
        {
            NativeGrpc = true,
            NativeMtls = true
        },
        ClientCertificate = new ConsoleClientCertificateBinding
        {
            Enabled = true,
            TrustProfileId = Options.TrustProfileId ?? string.Empty,
            Reference = new ConsoleClientCertificateReference
            {
                Kind = ConsoleClientCertificateReferenceKind.StoreThumbprint,
                Value = "integration"
            }
        }
    };

    public IConsoleClientCertificateValidationClient CreateValidationClient() =>
        new ServerClientCertificateValidationClient(new StaticTokenProvider(Options.AdminToken));

    public IConsoleServerCertificateProbe CreateProbe() => new TlsServerCertificateProbe();

    /// <summary>
    /// Builds the production trust gate (probe + validation client + evaluator + connection factory)
    /// over the supplied profile store and bound client certificate, so a test can drive a profile
    /// through the live validation path and then render the diagnostics surface from the persisted state.
    /// </summary>
    public ConsoleConnectionManager BuildConnectionManager(
        IConsoleEnvironmentProfileStore store,
        X509Certificate2 clientCertificate)
    {
        var resolver = new FixedCertificateResolver(clientCertificate);
        var tokenProvider = new StaticTokenProvider(Options.AdminToken);
        return new ConsoleConnectionManager(
            store,
            resolver,
            new TlsServerCertificateProbe(),
            new ServerClientCertificateValidationClient(tokenProvider),
            new ConsoleTrustEvaluator(),
            new NativeHonuaConnectionFactory(tokenProvider, resolver));
    }

    public X509Certificate2? LoadTrustedCertificate()
    {
        if (string.IsNullOrWhiteSpace(Options.TrustedCertificatePfxPath) || !File.Exists(Options.TrustedCertificatePfxPath))
        {
            return null;
        }

        return X509CertificateLoader.LoadPkcs12FromFile(
            Options.TrustedCertificatePfxPath,
            Options.TrustedCertificatePassword);
    }

    private static X509Certificate2 CreateSelfSignedCertificate(string subject)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(subject, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(30));
    }

    private sealed class StaticTokenProvider : IConsoleAccountTokenProvider
    {
        private readonly string? _token;

        public StaticTokenProvider(string? token) => _token = token;

        public ValueTask<string?> GetAccessTokenAsync(
            ConsoleEnvironmentProfile profile,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(_token);
    }

    private sealed class FixedCertificateResolver : IClientCertificateResolver
    {
        private readonly X509Certificate2 _certificate;

        public FixedCertificateResolver(X509Certificate2 certificate) => _certificate = certificate;

        public ValueTask<X509Certificate2?> ResolveAsync(
            ConsoleEnvironmentProfile profile,
            CancellationToken cancellationToken = default) =>
            // Return a fresh, caller-owned clone that retains the private key, so the live trust gate
            // exercises real client authentication (the server rejects the untrusted issuer) instead
            // of blocking locally on a missing private key.
            ValueTask.FromResult<X509Certificate2?>(profile.ClientCertificate.Enabled
                ? X509CertificateLoader.LoadPkcs12(_certificate.Export(X509ContentType.Pkcs12), password: null)
                : null);
    }
}
