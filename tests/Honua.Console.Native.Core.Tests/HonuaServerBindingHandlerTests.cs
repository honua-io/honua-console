using System.Net;
using System.Net.Http;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Security;
using Honua.Console.Shell.Services;
using Xunit;

namespace Honua.Console.Native.Core.Tests;

/// <summary>
/// Verifies the one binding model that unifies the Family-A honua-server clients
/// (honua-console#234): the active environment profile drives the target server at request time, and
/// the active operator's real bearer is forwarded (dropping the shared admin key), while a session
/// sentinel leaves the admin-key fallback in place.
/// </summary>
public sealed class HonuaServerBindingHandlerTests
{
    private static readonly Uri DiTimeBase = new("https://startup-server.example");

    [Fact]
    public async Task RetargetsRequestAuthorityToActiveProfile()
    {
        var capture = new CapturingHandler();
        var profiles = new InMemoryConsoleEnvironmentProfileStore(
            [Profile("env-a", "https://server-a.example", ConsoleAccountAuthMode.Anonymous)],
            activeProfileId: "env-a");
        using var client = BuildClient(capture, profiles, new InMemoryConsoleAccountSessionStore());

        _ = await client.GetAsync("/api/v1/console/content/");

        Assert.NotNull(capture.LastRequest);
        Assert.Equal("server-a.example", capture.LastRequest!.RequestUri!.Host);
        Assert.Equal("/api/v1/console/content/", capture.LastRequest.RequestUri.AbsolutePath);
    }

    [Fact]
    public async Task ForwardsOperatorBearerAndDropsAdminKey()
    {
        var capture = new CapturingHandler();
        var profiles = new InMemoryConsoleEnvironmentProfileStore(
            [Profile("env-a", "https://server-a.example", ConsoleAccountAuthMode.AccountRbac)],
            activeProfileId: "env-a");
        var sessions = new InMemoryConsoleAccountSessionStore();
        await sessions.SaveSessionAsync(new ConsoleAccountSession
        {
            ProfileId = "env-a",
            AccessToken = "real-operator-bearer"
        });
        using var client = BuildClient(capture, profiles, sessions);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/admin/version");
        request.Headers.TryAddWithoutValidation("X-API-Key", "shared-admin-key");
        _ = await client.SendAsync(request);

        Assert.Equal("Bearer", capture.LastRequest!.Headers.Authorization!.Scheme);
        Assert.Equal("real-operator-bearer", capture.LastRequest.Headers.Authorization.Parameter);
        Assert.False(capture.LastRequest.Headers.Contains("X-API-Key"));
    }

    [Fact]
    public async Task SessionSentinelKeepsAdminKeyFallback()
    {
        var capture = new CapturingHandler();
        var profiles = new InMemoryConsoleEnvironmentProfileStore(
            [Profile("env-a", "https://server-a.example", ConsoleAccountAuthMode.AccountRbac)],
            activeProfileId: "env-a");
        var sessions = new InMemoryConsoleAccountSessionStore();
        await sessions.SaveSessionAsync(new ConsoleAccountSession
        {
            ProfileId = "env-a",
            AccessToken = ConsoleAuthConstants.SessionSentinelPrefix + "env-a"
        });
        using var client = BuildClient(capture, profiles, sessions);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/admin/version");
        request.Headers.TryAddWithoutValidation("X-API-Key", "shared-admin-key");
        _ = await client.SendAsync(request);

        Assert.Null(capture.LastRequest!.Headers.Authorization);
        Assert.True(capture.LastRequest.Headers.Contains("X-API-Key"));
    }

    private static HttpClient BuildClient(
        CapturingHandler capture,
        IConsoleEnvironmentProfileStore profiles,
        IConsoleAccountSessionStore sessions)
    {
        var handler = new HonuaServerBindingHandler(profiles, sessions) { InnerHandler = capture };
        return new HttpClient(handler) { BaseAddress = DiTimeBase };
    }

    private static ConsoleEnvironmentProfile Profile(string id, string baseUrl, ConsoleAccountAuthMode mode) =>
        new()
        {
            Id = id,
            DisplayName = id,
            ServerBaseUri = new Uri(baseUrl),
            Account = new ConsoleAccountBinding { AuthMode = mode, AccountId = "op" }
        };

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
