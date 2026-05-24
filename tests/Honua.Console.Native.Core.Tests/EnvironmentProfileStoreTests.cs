using Honua.Console.Native.Core.Storage;
using Honua.Console.Shell.Models;

namespace Honua.Console.Native.Core.Tests;

public sealed class EnvironmentProfileStoreTests
{
    [Fact]
    public async Task NewNativeProfileStoreSeedsAtLeastTwoHonuaEnvironments()
    {
        var store = new JsonConsoleEnvironmentProfileStore(new InMemoryConsoleProfileStorage());

        var profiles = await store.ListProfilesAsync();

        Assert.Contains(profiles, profile => profile.Id == ConsoleEnvironmentProfileDefaults.DevelopmentProfileId);
        Assert.Contains(profiles, profile => profile.Id == ConsoleEnvironmentProfileDefaults.StagingProfileId);
        Assert.Contains(profiles, profile => profile.TransportCapabilities.NativeGrpc);
        Assert.Contains(profiles, profile => profile.ClientCertificate.Enabled);
    }

    [Fact]
    public async Task NativeProfileStorePersistsDistinctEnvironmentState()
    {
        var storage = new InMemoryConsoleProfileStorage();
        var store = new JsonConsoleEnvironmentProfileStore(storage);

        var devProfile = CreateProfile("dev-east", "https://dev-east.honua.example", nativeMtls: false);
        var prodProfile = CreateProfile("prod-west", "https://prod-west.honua.example", nativeMtls: true);

        await store.UpsertProfileAsync(devProfile);
        await store.UpsertProfileAsync(prodProfile);
        await store.SaveStateAsync(new ConsoleEnvironmentState
        {
            ProfileId = devProfile.Id,
            LastRoute = "/studio",
            LastStreamingResumeToken = "dev-resume-42"
        });
        await store.SaveStateAsync(new ConsoleEnvironmentState
        {
            ProfileId = prodProfile.Id,
            LastRoute = "/operate/native-stream",
            LastStreamingResumeToken = "prod-resume-99"
        });
        await store.ActivateProfileAsync(prodProfile.Id);

        var reloaded = new JsonConsoleEnvironmentProfileStore(storage);
        var active = await reloaded.GetActiveProfileAsync();
        var devState = await reloaded.GetStateAsync(devProfile.Id);
        var prodState = await reloaded.GetStateAsync(prodProfile.Id);

        Assert.Equal(prodProfile.Id, active?.Id);
        Assert.Equal("/studio", devState?.LastRoute);
        Assert.Equal("dev-resume-42", devState?.LastStreamingResumeToken);
        Assert.Equal("/operate/native-stream", prodState?.LastRoute);
        Assert.Equal("prod-resume-99", prodState?.LastStreamingResumeToken);
    }

    private static ConsoleEnvironmentProfile CreateProfile(
        string id,
        string serverBaseUri,
        bool nativeMtls) =>
        new()
        {
            Id = id,
            DisplayName = id,
            ServerBaseUri = new Uri(serverBaseUri),
            EnvironmentKind = nativeMtls ? "production" : "development",
            TenantId = $"{id}-tenant",
            TransportCapabilities = new ConsoleEnvironmentTransportCapabilities
            {
                BrowserHttp = true,
                BrowserRealtime = true,
                NativeGrpc = true,
                NativeMtls = nativeMtls
            },
            Account = new ConsoleAccountBinding
            {
                AuthMode = ConsoleAccountAuthMode.AccountRbac,
                AccountId = $"{id}-operator",
                TenantId = $"{id}-tenant",
                PermissionHints = ["catalog:read", "operate:jobs:read"]
            },
            ClientCertificate = nativeMtls
                ? new ConsoleClientCertificateBinding
                {
                    Enabled = true,
                    Reference = new ConsoleClientCertificateReference
                    {
                        Kind = ConsoleClientCertificateReferenceKind.StoreThumbprint,
                        Value = "CERT-THUMBPRINT"
                    }
                }
                : new ConsoleClientCertificateBinding()
        };
}
