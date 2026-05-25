using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Honua.Console.Native.Core.Connections;
using Honua.Console.Native.Core.Security;
using Honua.Console.Native.Core.Storage;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.Native.Core.Tests;

public sealed class NativeConnectionFactoryTests
{
    [Fact]
    public async Task CreateAsyncUsesAccountRbacBearerTokenAndEnvironmentCertificate()
    {
        using var certificate = CreateCertificate();
        var profile = CreateProfile(clientCertificateEnabled: true);
        var sessions = new JsonConsoleAccountSessionStore(new InMemoryNativeSecretStore());
        await sessions.SaveSessionAsync(new ConsoleAccountSession
        {
            ProfileId = profile.Id,
            AccountId = "operator-1",
            DisplayName = "Operator One",
            TenantId = profile.TenantId,
            RoleIds = ["operator"],
            PermissionIds = ["operate:telemetry:read"],
            AccessToken = "account-rbac-token"
        });

        var factory = new NativeHonuaConnectionFactory(
            new NativeSecretStoreAccountTokenProvider(sessions),
            new StaticCertificateResolver(certificate));

        await using var connection = await factory.CreateAsync(profile);

        Assert.Equal(profile.ServerBaseUri, connection.HttpClient.BaseAddress);
        Assert.Equal("Bearer", connection.HttpClient.DefaultRequestHeaders.Authorization?.Scheme);
        Assert.Equal("account-rbac-token", connection.HttpClient.DefaultRequestHeaders.Authorization?.Parameter);
        Assert.Equal("account-rbac-token", connection.BearerToken);
        Assert.Same(certificate, connection.ClientCertificate);
        Assert.NotNull(connection.GrpcChannel);
    }

    [Fact]
    public async Task CreateAsyncOmitsNativeOnlyCredentialsForAnonymousProfiles()
    {
        var profile = CreateProfile(clientCertificateEnabled: false) with
        {
            Account = new ConsoleAccountBinding
            {
                AuthMode = ConsoleAccountAuthMode.Anonymous,
                TenantId = "public"
            }
        };
        var sessions = new JsonConsoleAccountSessionStore(new InMemoryNativeSecretStore());
        var factory = new NativeHonuaConnectionFactory(
            new NativeSecretStoreAccountTokenProvider(sessions),
            new StaticCertificateResolver(null));

        await using var connection = await factory.CreateAsync(profile);

        Assert.Null(connection.HttpClient.DefaultRequestHeaders.Authorization);
        Assert.Null(connection.BearerToken);
        Assert.Null(connection.ClientCertificate);
    }

    [Fact]
    public async Task CreateAsyncUsesTrustedServerFingerprintForNativeHttpTransport()
    {
        using var serverCertificate = CreateServerCertificate();
        await using var server = OneShotHttpsServer.Start(serverCertificate);
        var profile = CreateProfile(clientCertificateEnabled: false) with { ServerBaseUri = server.BaseUri };
        var trustedFingerprint = NativeServerTrust.ComputeSha256Fingerprint(serverCertificate);
        var factory = new NativeHonuaConnectionFactory(
            new EmptyTokenProvider(),
            new StaticCertificateResolver(null));

        await using var connection = await factory.CreateAsync(profile, trustedFingerprint);
        using var response = await connection.HttpClient.GetAsync("/");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("ok", body);
    }

    private static ConsoleEnvironmentProfile CreateProfile(bool clientCertificateEnabled) =>
        new()
        {
            Id = "staging",
            DisplayName = "Staging",
            ServerBaseUri = new Uri("https://staging.honua.example"),
            TenantId = "tenant-staging",
            TransportCapabilities = new ConsoleEnvironmentTransportCapabilities
            {
                BrowserHttp = true,
                BrowserRealtime = true,
                NativeGrpc = true,
                NativeMtls = clientCertificateEnabled
            },
            Account = new ConsoleAccountBinding
            {
                AuthMode = ConsoleAccountAuthMode.AccountRbac,
                AccountId = "operator-1",
                TenantId = "tenant-staging"
            },
            ClientCertificate = clientCertificateEnabled
                ? new ConsoleClientCertificateBinding
                {
                    Enabled = true,
                    Reference = new ConsoleClientCertificateReference
                    {
                        Kind = ConsoleClientCertificateReferenceKind.StoreThumbprint,
                        Value = "ABC123"
                    }
                }
                : new ConsoleClientCertificateBinding()
        };

    private static X509Certificate2 CreateCertificate()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=Honua Test Operator", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
    }

    private static X509Certificate2 CreateServerCertificate()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=Honua Test Server", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var subjectAlternativeNames = new SubjectAlternativeNameBuilder();
        subjectAlternativeNames.AddIpAddress(IPAddress.Loopback);
        subjectAlternativeNames.AddDnsName("localhost");
        request.CertificateExtensions.Add(subjectAlternativeNames.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
            false));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

        using var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddDays(1));
        return X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pkcs12), password: null);
    }

    private sealed class StaticCertificateResolver : IClientCertificateResolver
    {
        private readonly X509Certificate2? _certificate;

        public StaticCertificateResolver(X509Certificate2? certificate)
        {
            _certificate = certificate;
        }

        public ValueTask<X509Certificate2?> ResolveAsync(
            ConsoleEnvironmentProfile profile,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(profile.ClientCertificate.Enabled ? _certificate : null);
        }
    }

    private sealed class EmptyTokenProvider : IConsoleAccountTokenProvider
    {
        public ValueTask<string?> GetAccessTokenAsync(
            ConsoleEnvironmentProfile profile,
            CancellationToken cancellationToken = default) => ValueTask.FromResult<string?>(null);
    }

    private sealed class OneShotHttpsServer : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly X509Certificate2 _certificate;
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _runTask;

        private OneShotHttpsServer(TcpListener listener, X509Certificate2 certificate, Uri baseUri)
        {
            _listener = listener;
            _certificate = certificate;
            BaseUri = baseUri;
            _runTask = RunAsync();
        }

        public Uri BaseUri { get; }

        public static OneShotHttpsServer Start(X509Certificate2 certificate)
        {
            var listener = new TcpListener(IPAddress.Loopback, port: 0);
            listener.Start();
            var endpoint = (IPEndPoint)listener.LocalEndpoint;
            return new OneShotHttpsServer(listener, certificate, new Uri($"https://127.0.0.1:{endpoint.Port}/"));
        }

        public async ValueTask DisposeAsync()
        {
            if (!_runTask.IsCompleted)
            {
                await _stop.CancelAsync();
                _listener.Stop();
            }

            try
            {
                await _runTask.ConfigureAwait(false);
            }
            finally
            {
                _stop.Dispose();
            }
        }

        private async Task RunAsync()
        {
            try
            {
                using var tcp = await _listener.AcceptTcpClientAsync(_stop.Token).ConfigureAwait(false);
                await using var network = tcp.GetStream();
                using var tls = new SslStream(network, leaveInnerStreamOpen: false);
                await tls
                    .AuthenticateAsServerAsync(
                        new SslServerAuthenticationOptions
                        {
                            ServerCertificate = _certificate,
                            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
                        },
                        _stop.Token)
                    .ConfigureAwait(false);

                await ReadRequestHeadersAsync(tls, _stop.Token).ConfigureAwait(false);

                var body = Encoding.UTF8.GetBytes("ok");
                var headers = Encoding.ASCII.GetBytes(
                    $"HTTP/1.1 200 OK\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
                await tls.WriteAsync(headers, _stop.Token).ConfigureAwait(false);
                await tls.WriteAsync(body, _stop.Token).ConfigureAwait(false);
                await tls.FlushAsync(_stop.Token).ConfigureAwait(false);
            }
            catch (Exception) when (_stop.IsCancellationRequested)
            {
            }
            finally
            {
                _listener.Stop();
            }
        }

        private static async Task ReadRequestHeadersAsync(Stream stream, CancellationToken cancellationToken)
        {
            var terminator = "\r\n\r\n"u8.ToArray();
            var buffer = new byte[1];
            var matched = 0;
            while (matched < terminator.Length)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    return;
                }

                matched = buffer[0] == terminator[matched]
                    ? matched + 1
                    : buffer[0] == terminator[0] ? 1 : 0;
            }
        }
    }
}
