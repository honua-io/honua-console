using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Honua.Console.Native.Core.Connections;
using Honua.Console.Native.Core.Security;
using Honua.Console.Native.Core.Storage;
using Honua.Console.Shell.Models;

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
}
