using System.Security.Cryptography;
using System.Text.Json;
using Bunit;
using Honua.Console.Contracts;
using Honua.Console.Shell.Components;
using Microsoft.Extensions.DependencyInjection;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;

namespace Honua.Console.IntegrationTests;

public sealed class LicenseExpiryBannerIntegrationTests
{
    [SkippableFact]
    public async Task PaidServer_ActualLicenseStatusRendersExpiryWarning()
    {
        var options = ConsoleTrustIntegrationOptions.Load();
        Skip.IfNot(options.Enabled, "Enable HONUA_CONSOLE_INTEGRATION to run the license banner container proof.");
        Skip.If(string.IsNullOrWhiteSpace(options.ServerImage), "Set HONUA_CONSOLE_SERVER_IMAGE to the strict-license server candidate.");
        Skip.If(ConsoleTrustIntegrationOptions.GetSkipReason() is not null,
            ConsoleTrustIntegrationOptions.GetSkipReason());

        // Both the signing key and license are generated for this test only.
        var privateKey = new Ed25519PrivateKeyParameters(new SecureRandom());
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schema = "honua.license/v1",
            licenseId = "synthetic-console-banner",
            licensedTo = "Synthetic Test",
            edition = "Pro",
            issuedAt = DateTimeOffset.UtcNow.AddDays(-1),
            expiresAt = DateTimeOffset.UtcNow.AddDays(14).AddMinutes(-1),
            entitlements = Array.Empty<string>()
        });
        var signer = new Ed25519Signer();
        signer.Init(true, privateKey);
        signer.BlockUpdate(payload, 0, payload.Length);
        var envelope = JsonSerializer.Serialize(new
        {
            version = 1,
            keyId = "synthetic",
            payload = Base64Url(payload),
            signature = Base64Url(signer.GenerateSignature())
        });
        var adminKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        options = options with
        {
            ExternalBaseUri = null,
            ServerScheme = "http",
            ServerHealthPath = "/healthz/ready",
            StudioAdminApiKey = adminKey,
            ServerEnvironment = string.Join('\n', options.ServerEnvironment,
                "ASPNETCORE_ENVIRONMENT=Development",
                "HONUA_ADMIN_API_KEY=" + adminKey,
                "HONUA_ADMIN_PASSWORD=" + adminKey,
                "Licensing__Edition=Pro",
                "Licensing__LicenseContent=" + envelope,
                "Licensing__TrustedKeys__synthetic=base64url:" + Base64Url(privateKey.GeneratePublicKey().GetEncoded()))
        };

        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        await using var server = await HonuaServerTestcontainer.StartAsync(options, timeout.Token);
        using var client = new HonuaAdminOperateHttpClient(new HttpClient(),
            new HonuaAdminOperateClientOptions(server.BaseAddress, adminKey));
        var status = await client.GetLicenseStatusAsync(timeout.Token);
        Assert.Equal("Pro", status.Data?.Edition);
        Assert.True(status.Data?.IsValid);

        await using var context = new BunitContext();
        context.Services.AddSingleton<IHonuaAdminOperateClient>(client);
        var component = context.Render<LicenseExpiryBanner>();
        component.WaitForAssertion(() =>
        {
            Assert.Equal("14", component.Find("[role=alert]").GetAttribute("data-license-warning-days"));
            Assert.Contains("backup/export before expiry", component.Markup);
            Assert.Contains("Reads and exports stop", component.Markup);
        }, TimeSpan.FromSeconds(10));
    }

    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
