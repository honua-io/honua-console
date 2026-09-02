using Honua.Console.Shell.DependencyInjection;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;
using Honua.Sdk.Studio.Capabilities;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.Native.Core.Tests;

/// <summary>
/// Host-independent coverage for the registry-driven Studio-AI intent resolution path (honua-console#266):
/// the capability registry client, the intent resolver, and the Studio:RegistryIntentResolution flag DI
/// gate. Flag OFF (default) preserves current behavior via the no-op resolver; flag ON + a bound server
/// resolves the generate/validate/preview/publish lifecycle against the live capability-manifest registry
/// and hides deferred/unavailable capabilities. Uses a stub manifest client (a test double, never a
/// standing production mock).
/// </summary>
public sealed class StudioIntentResolverTests
{
    private const string StudioPrompt = "publish Maui parcels as a feature service";
    private const string ServerBaseUrl = "https://server.honua.example";

    // A capability manifest advertising: generate/preview available, publish supported-but-gated
    // (deferred), validate unsupported; the "map" family supported and the "report" family deferred.
    private static CapabilityManifest BuildManifest() => new()
    {
        Capabilities =
        [
            new CapabilityEntry { Id = StudioCapabilityIds.Generate, Available = true, Supported = true },
            new CapabilityEntry { Id = StudioCapabilityIds.Preview, Available = true, Supported = true },
            new CapabilityEntry
            {
                Id = StudioCapabilityIds.Publish,
                Available = false,
                Supported = true,
                ReasonCode = "entitlement_required",
            },
            new CapabilityEntry
            {
                Id = StudioCapabilityIds.Validate,
                Available = false,
                Supported = false,
                ReasonCode = "not_implemented",
            },
        ],
        Packages = new CapabilityManifestPackages
        {
            Families =
            [
                new CapabilityPackageFamily { Id = "map", Supported = true },
                new CapabilityPackageFamily { Id = "report", Supported = false },
            ],
        },
    };

    private static StudioIntentResolver BuildResolver(IHonuaCapabilityManifestClient manifestClient) =>
        new(new OmniPromptIntentClassifier(), new HonuaServerCapabilityRegistryClient(manifestClient));

    [Fact]
    public async Task Resolve_AvailableCapabilityAndSupportedFamily_ResolvesToDescriptor()
    {
        var resolver = BuildResolver(new StubManifestClient(BuildManifest()));

        var result = await resolver.ResolveAsync(
            new StudioIntentRequest(StudioPrompt, "map", StudioLifecyclePhase.Generate));

        Assert.True(result.Succeeded);
        Assert.False(result.Hidden);
        Assert.Equal("Resolved", result.State);
        Assert.NotNull(result.Capability);
        Assert.Equal(StudioCapabilityIds.Generate, result.Capability!.Id);
        Assert.True(result.Capability.Available);
    }

    [Fact]
    public async Task Resolve_DeferredCapability_IsHiddenAsUnavailable()
    {
        var resolver = BuildResolver(new StubManifestClient(BuildManifest()));

        // studio.publish is supported but not available in scope (deferred) → hidden, not an exception.
        var result = await resolver.ResolveAsync(
            new StudioIntentRequest(StudioPrompt, "map", StudioLifecyclePhase.Publish));

        Assert.False(result.Succeeded);
        Assert.True(result.Hidden);
        Assert.Equal("Unavailable", result.State);
        Assert.Null(result.Capability);
        Assert.False(string.IsNullOrWhiteSpace(result.Detail));
    }

    [Fact]
    public async Task Resolve_UnsupportedCapability_IsHiddenAsUnsupported()
    {
        var resolver = BuildResolver(new StubManifestClient(BuildManifest()));

        var result = await resolver.ResolveAsync(
            new StudioIntentRequest(StudioPrompt, "map", StudioLifecyclePhase.Validate));

        Assert.False(result.Succeeded);
        Assert.True(result.Hidden);
        Assert.Equal("Unsupported", result.State);
    }

    [Fact]
    public async Task Resolve_DeferredPackageFamily_HidesEveryPhase()
    {
        var resolver = BuildResolver(new StubManifestClient(BuildManifest()));

        // The "report" family is advertised unsupported (deferred), so even an available phase is hidden.
        var result = await resolver.ResolveAsync(
            new StudioIntentRequest(StudioPrompt, "report", StudioLifecyclePhase.Generate));

        Assert.False(result.Succeeded);
        Assert.True(result.Hidden);
        Assert.Equal("Unavailable", result.State);
    }

    [Fact]
    public async Task Resolve_HighConfidenceDevOpsPrompt_IsHiddenAsRejected()
    {
        var resolver = BuildResolver(new StubManifestClient(BuildManifest()));

        var result = await resolver.ResolveAsync(
            new StudioIntentRequest("roll back staging to the last good revision", "map", StudioLifecyclePhase.Generate));

        Assert.False(result.Succeeded);
        Assert.True(result.Hidden);
        Assert.Equal("Rejected", result.State);
    }

    [Fact]
    public async Task Resolve_MissingBindingRegistry_YieldsMissingBindingOutcome()
    {
        var resolver = new StudioIntentResolver(
            new OmniPromptIntentClassifier(), new UnsupportedCapabilityRegistryClient());

        var result = await resolver.ResolveAsync(
            new StudioIntentRequest(StudioPrompt, "map", StudioLifecyclePhase.Generate));

        Assert.False(result.Succeeded);
        Assert.Equal("Missing binding", result.State);
        Assert.False(string.IsNullOrWhiteSpace(result.Detail));
    }

    [Fact]
    public async Task NoopResolver_ResolvesEveryPhaseAsAvailable_WithoutRegistryGating()
    {
        var resolver = new NoopStudioIntentResolver(new OmniPromptIntentClassifier());

        // Even the publish phase (deferred under the registry) resolves as available with no gating.
        var result = await resolver.ResolveAsync(
            new StudioIntentRequest(StudioPrompt, "map", StudioLifecyclePhase.Publish));

        Assert.True(result.Succeeded);
        Assert.False(result.Hidden);
        Assert.NotNull(result.Capability);
        Assert.True(result.Capability!.Available);
    }

    [Fact]
    public async Task ServerRegistryClient_ProjectsManifestIntoSnapshot()
    {
        var client = new HonuaServerCapabilityRegistryClient(new StubManifestClient(BuildManifest()));

        var snapshot = await client.GetSnapshotAsync();

        Assert.True(snapshot.Bound);
        Assert.Equal("Resolved", snapshot.State);
        Assert.True(snapshot.IsAvailable(StudioCapabilityIds.Generate));
        Assert.False(snapshot.IsAvailable(StudioCapabilityIds.Publish));
        Assert.True(snapshot.IsSupported(StudioCapabilityIds.Publish));
        Assert.True(snapshot.HasPackageFamily("map"));
        // An advertised-but-unsupported family is projected as absent (deferred).
        Assert.False(snapshot.HasPackageFamily("report"));
    }

    [Fact]
    public async Task ServerRegistryClient_ManifestReadFailure_YieldsUnavailableSnapshot_NotException()
    {
        var client = new HonuaServerCapabilityRegistryClient(
            new ThrowingManifestClient(new HttpRequestException("connection refused")));

        var snapshot = await client.GetSnapshotAsync();

        Assert.False(snapshot.Bound);
        Assert.Equal("Unavailable", snapshot.State);
        Assert.Empty(snapshot.Descriptors);
    }

    [Fact]
    public async Task UnsupportedRegistryClient_ReturnsMissingBindingSnapshot()
    {
        var snapshot = await new UnsupportedCapabilityRegistryClient().GetSnapshotAsync();

        Assert.False(snapshot.Bound);
        Assert.Equal("Missing binding", snapshot.State);
        Assert.Empty(snapshot.Descriptors);
        Assert.Empty(snapshot.PackageFamilies);
        Assert.False(string.IsNullOrWhiteSpace(snapshot.Detail));
    }

    [Fact]
    public void FlagOff_RegistersNoopResolverButKeepsLiveRegistryForConsoleGates()
    {
        using var provider = new ServiceCollection()
            .AddHonuaConsoleShell(honuaServerBaseUrl: ServerBaseUrl, registryIntentResolutionEnabled: false)
            .BuildServiceProvider();

        Assert.IsType<NoopStudioIntentResolver>(provider.GetRequiredService<IStudioIntentResolver>());
        Assert.IsType<HonuaServerCapabilityRegistryClient>(provider.GetRequiredService<ICapabilityRegistryClient>());
        Assert.IsType<ManifestBackedConsoleCapabilityManifest>(provider.GetRequiredService<IConsoleCapabilityManifest>());
    }

    [Fact]
    public void FlagOn_WithServerBound_RegistersRegistryBackedResolver()
    {
        using var provider = new ServiceCollection()
            .AddHonuaConsoleShell(honuaServerBaseUrl: ServerBaseUrl, registryIntentResolutionEnabled: true)
            .BuildServiceProvider();

        Assert.IsType<StudioIntentResolver>(provider.GetRequiredService<IStudioIntentResolver>());
        Assert.IsType<HonuaServerCapabilityRegistryClient>(provider.GetRequiredService<ICapabilityRegistryClient>());
    }

    [Fact]
    public void FlagOn_WithoutServer_FallsBackToNoopResolver()
    {
        using var provider = new ServiceCollection()
            .AddHonuaConsoleShell(honuaServerBaseUrl: null, registryIntentResolutionEnabled: true)
            .BuildServiceProvider();

        Assert.IsType<NoopStudioIntentResolver>(provider.GetRequiredService<IStudioIntentResolver>());
        Assert.IsType<UnsupportedCapabilityRegistryClient>(provider.GetRequiredService<ICapabilityRegistryClient>());
    }

    private sealed class StubManifestClient : IHonuaCapabilityManifestClient
    {
        private readonly CapabilityManifest _manifest;

        public StubManifestClient(CapabilityManifest manifest) => _manifest = manifest;

        public Task<CapabilityManifest> GetManifestAsync(
            string? environment = null,
            string? workspaceId = null,
            CancellationToken cancellationToken = default) => Task.FromResult(_manifest);
    }

    private sealed class ThrowingManifestClient : IHonuaCapabilityManifestClient
    {
        private readonly Exception _exception;

        public ThrowingManifestClient(Exception exception) => _exception = exception;

        public Task<CapabilityManifest> GetManifestAsync(
            string? environment = null,
            string? workspaceId = null,
            CancellationToken cancellationToken = default) => Task.FromException<CapabilityManifest>(_exception);
    }
}
