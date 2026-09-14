using System.Net;
using System.Text;
using Bunit;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.Layout;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;
using Honua.Sdk.Studio.Capabilities;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class LiveManifestBindingTests
{
    [Fact]
    public async Task ActiveProfileSwitch_RetargetsSdkRequestAndUpdatesExistingNavigation()
    {
        using var ctx = new BunitContext();
        ctx.AddConsoleNotifications();
        var profiles = new InMemoryConsoleEnvironmentProfileStore([
            new ConsoleEnvironmentProfile { Id = "one", ServerBaseUri = new Uri("https://one.example") },
            new ConsoleEnvironmentProfile { Id = "two", ServerBaseUri = new Uri("https://two.example") },
        ], activeProfileId: "one");
        var sessions = new InMemoryConsoleAccountSessionStore();
        await sessions.SaveSessionAsync(new ConsoleAccountSession { ProfileId = "one", AccessToken = "operator-one" });
        await sessions.SaveSessionAsync(new ConsoleAccountSession { ProfileId = "two", AccessToken = "operator-two" });
        var server = new BindingHandler();
        using var http = new HttpClient(new HonuaServerBindingHandler(profiles, sessions) { InnerHandler = server })
        {
            BaseAddress = new Uri("https://startup.example"),
        };
        var manifest = new ManifestBackedConsoleCapabilityManifest(
            new HonuaServerCapabilityRegistryClient(new HonuaCapabilityManifestClient(http)));
        ctx.Services.AddSingleton<IConsoleCapabilityManifest>(manifest);
        ctx.Services.AddSingleton<IConsoleHostCapabilities, BrowserConsoleHostCapabilities>();
        ctx.Services.AddSingleton<IConsoleProductMode>(
            new ConfiguredConsoleProductMode(ConsoleProductMode.Full));
        ctx.Services.GetRequiredService<NavigationManager>().NavigateTo("/operate");
        var layout = ctx.Render<ConsoleLayout>();
        layout.WaitForAssertion(
            () => Assert.True(manifest.IsAdvertised(ConsoleCapabilityKeys.Temporal)),
            TimeSpan.FromSeconds(5));
        Assert.False(manifest.IsAdvertised(ConsoleCapabilityKeys.DisconnectedSync));

        // #338 (src/Honua.Console.Shell/Layout/ConsoleLayout.razor, OperateSections) rewrote the
        // focused Operate nav rail as a fixed list with no per-item capability filter, so
        // /operate/temporal and /operate/sync are no longer nav-rail links at all — gated or
        // otherwise. There is no remaining nav-rail entry to assert manifest-driven visibility on;
        // #365's "no page keeps a second allowlist path" denominator is satisfied by #338 removing
        // the allowlist, not by nav coverage here. The manifest-driven observable that survives is
        // the page-level gate every capability-gated Operate route still renders through
        // ConsoleCapabilityGate (e.g. OperateTemporalPage, OperateSyncPage), so assert that directly
        // against this same live, server-refreshed manifest instance across the profile switch.
        var temporalGateBefore = ctx.Render<ConsoleCapabilityGate>(parameters => parameters
            .Add(p => p.Capability, ConsoleCapabilityKeys.Temporal)
            .AddChildContent("<div data-live=\"1\">temporal surface</div>"));
        Assert.Contains("temporal surface", temporalGateBefore.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("console-state-unsupported", temporalGateBefore.Markup, StringComparison.Ordinal);

        await profiles.ActivateProfileAsync("two");
        await manifest.RefreshAsync();

        Assert.False(manifest.IsAdvertised(ConsoleCapabilityKeys.Temporal));
        Assert.True(manifest.IsAdvertised(ConsoleCapabilityKeys.DisconnectedSync));

        var temporalGateAfter = ctx.Render<ConsoleCapabilityGate>(parameters => parameters
            .Add(p => p.Capability, ConsoleCapabilityKeys.Temporal)
            .AddChildContent("<div data-live=\"1\">temporal surface</div>"));
        Assert.Contains("console-state-unsupported", temporalGateAfter.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("temporal surface", temporalGateAfter.Markup, StringComparison.Ordinal);

        var syncGateAfter = ctx.Render<ConsoleCapabilityGate>(parameters => parameters
            .Add(p => p.Capability, ConsoleCapabilityKeys.DisconnectedSync)
            .AddChildContent("<div data-live=\"1\">sync surface</div>"));
        Assert.Contains("sync surface", syncGateAfter.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("console-state-unsupported", syncGateAfter.Markup, StringComparison.Ordinal);

        Assert.Equal(new[] { "one.example", "two.example" }, server.Requests.Select(request => request.Host));
        Assert.Equal(new[] { "operator-one", "operator-two" }, server.Requests.Select(request => request.Bearer));
        Assert.All(server.Requests, request => Assert.Equal("/api/v1/capabilities/manifest", request.Path));
    }

    private sealed class BindingHandler : HttpMessageHandler
    {
        public List<(string Host, string? Bearer, string Path)> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            Requests.Add((uri.Host, request.Headers.Authorization?.Parameter, uri.AbsolutePath));
            var id = uri.Host == "one.example" ? "temporal.filtering" : "sync.offline";
            var body = $$"""{"schemaVersion":"honua.capability_manifest.v1","capabilities":[{"id":"{{id}}","supported":true,"available":true}]}""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }
}
