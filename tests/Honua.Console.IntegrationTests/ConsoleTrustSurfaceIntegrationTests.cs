using Bunit;
using Honua.Console.Native.Core.Connections;
using Honua.Console.Native.Core.Storage;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Asserts the Console environment-diagnostics surface renders from live trust data (AC#4, Console
/// Patterns Charter section 11): a profile is driven through the real trust gate against a live
/// honua-server, then the shared <see cref="EnvironmentProfileDetailPage"/> is rendered and asserted
/// to surface the server-validated trust state. Skips gracefully when the opt-in env/Docker/admin
/// token is unavailable.
/// </summary>
[Collection(ConsoleTrustIntegrationCollection.Name)]
public sealed class ConsoleTrustSurfaceIntegrationTests
{
    private readonly HonuaServerMtlsFixture _fixture;

    public ConsoleTrustSurfaceIntegrationTests(HonuaServerMtlsFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task DiagnosticsSurface_RendersLiveTrustState_FromRealServerValidation()
    {
        var skipReason = ConsoleTrustIntegrationOptions.GetValidateSkipReason();
        Skip.If(skipReason is not null, skipReason ?? string.Empty);

        const string profileId = "integration";
        var store = new JsonConsoleEnvironmentProfileStore(new InMemoryConsoleProfileStorage());
        await store.UpsertProfileAsync(_fixture.BuildProfile(profileId));

        // Drive the profile through the real probe + validation client + trust gate against the live
        // server. An untrusted client certificate must yield a blocking trust state.
        await using var manager = _fixture.BuildConnectionManager(store, _fixture.UntrustedClientCertificate);
        var outcome = await manager.ConnectAsync(profileId);
        Skip.If(
            outcome.Status == ConsoleConnectionStatus.Unreachable,
            "The live server could not be reached/authorized to validate the certificate.");

        Assert.Equal(ConsoleConnectionStatus.Blocked, outcome.Status);

        var state = await store.GetStateAsync(profileId);
        Assert.NotNull(state);
        Assert.True(state!.TrustBlocked);
        Assert.NotNull(state.Trust);

        // Render the shared diagnostics surface against the live-validated state.
        using var ctx = new Bunit.TestContext();
        ctx.Services.AddSingleton<IConsoleEnvironmentProfileStore>(store);
        ctx.Services.AddSingleton<IConsoleHostCapabilities>(new NativeConsoleHostCapabilities());
        ctx.Services.AddSingleton<IConsoleConnectionManager>(manager);

        var component = ctx.RenderComponent<EnvironmentProfileDetailPage>(
            parameters => parameters.Add(page => page.ProfileId, profileId));
        var markup = component.Markup;

        // The surface reflects the live blocking trust state and its server-validated status.
        Assert.Contains("Connection blocked", markup, StringComparison.Ordinal);
        Assert.Contains(state.Trust!.Status.ToString(), markup, StringComparison.Ordinal);
    }
}
