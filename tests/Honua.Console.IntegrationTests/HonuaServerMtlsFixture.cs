using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using Honua.Console.Native.Core.Security;
using Honua.Console.Shell.Models;
using Testcontainers.PostgreSql;

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
    private INetwork? _network;
    private PostgreSqlContainer? _postgres;
    private IContainer? _server;

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

        _network = new NetworkBuilder().Build();

        _postgres = new PostgreSqlBuilder()
            .WithImage("postgis/postgis:16-3.4")
            .WithNetwork(_network)
            .WithNetworkAliases("postgres")
            .WithDatabase("honua")
            .WithUsername("honua")
            .WithPassword("honua")
            .Build();
        await _postgres.StartAsync().ConfigureAwait(false);

        var connectionString = "Host=postgres;Port=5432;Database=honua;Username=honua;Password=honua";

        var useTls = string.Equals(Options.ServerScheme, "https", StringComparison.OrdinalIgnoreCase);
        var builder = new ContainerBuilder(Options.ServerImage!)
            .WithNetwork(_network)
            .WithPortBinding(Options.ServerPort, assignRandomHostPort: true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request =>
            {
                request = request
                    .ForPort(Options.ServerPort)
                    .ForPath(Options.ServerHealthPath)
                    .ForStatusCodeMatching(code => (int)code is >= 200 and < 500);

                if (useTls)
                {
                    // The fixture server presents a self-signed/dev certificate; accept it for the readiness probe only.
                    request = request
                        .UsingTls()
                        .UsingHttpMessageHandler(new HttpClientHandler
                        {
                            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
                        });
                }

                return request;
            }));

        foreach (var (key, value) in Options.BuildServerEnvironment(connectionString))
        {
            builder = builder.WithEnvironment(key, value);
        }

        _server = builder.Build();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        await _server.StartAsync(timeout.Token).ConfigureAwait(false);

        BaseAddress = new UriBuilder(
            Options.ServerScheme,
            _server.Hostname,
            _server.GetMappedPublicPort(Options.ServerPort)).Uri;
    }

    public async Task DisposeAsync()
    {
        if (_server is not null)
        {
            await _server.DisposeAsync().ConfigureAwait(false);
        }

        if (_postgres is not null)
        {
            await _postgres.DisposeAsync().ConfigureAwait(false);
        }

        if (_network is not null)
        {
            await _network.DisposeAsync().ConfigureAwait(false);
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
}
