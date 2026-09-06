using System.Net;
using System.Text;
using Bunit;
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
        ctx.Services.GetRequiredService<NavigationManager>().NavigateTo("/operate");
        var layout = ctx.Render<ConsoleLayout>();
        layout.WaitForAssertion(() => Assert.Contains("href=\"/operate/temporal\"", layout.Markup, StringComparison.Ordinal));
        Assert.DoesNotContain("href=\"/operate/sync\"", layout.Markup, StringComparison.Ordinal);

        await profiles.ActivateProfileAsync("two");
        await manifest.RefreshAsync();

        layout.WaitForAssertion(() =>
        {
            Assert.DoesNotContain("href=\"/operate/temporal\"", layout.Markup, StringComparison.Ordinal);
            Assert.Contains("href=\"/operate/sync\"", layout.Markup, StringComparison.Ordinal);
        });
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
