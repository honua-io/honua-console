using System.Net.Http.Headers;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Security;
using Honua.Console.Shell.Services;
using Xunit;

namespace Honua.Console.Native.Core.Tests;

/// <summary>
/// Guards the single auth + URI rule shared by the Family-B honua-server clients
/// (observability, scenes, SensorThings, support, etc.). The non-forwardable Console
/// session sentinel must resolve to <see langword="null"/> so the admin-key fallback
/// engages — mirroring <see cref="HonuaServerBindingHandler"/> — instead of being
/// forwarded as a Bearer token (which 401/403-ed every Family-B surface before the fix).
/// </summary>
public sealed class ConsoleServerHttpTests
{
    [Fact]
    public async Task AttachAuthentication_PrefersOperatorBearerOverConfiguredAdminKey()
    {
        var sessions = new InMemoryConsoleAccountSessionStore();
        await sessions.SaveSessionAsync(new ConsoleAccountSession
        {
            ProfileId = "env-a",
            AccessToken = "real-operator-bearer"
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://server.example/approve");

        await ConsoleServerHttp.AttachAuthenticationAsync(
            request,
            sessions,
            Profile("env-a", ConsoleAccountAuthMode.AccountRbac),
            "shared-admin-key",
            CancellationToken.None);

        Assert.Equal(
            new AuthenticationHeaderValue("Bearer", "real-operator-bearer"),
            request.Headers.Authorization);
        Assert.False(request.Headers.Contains("X-API-Key"));
    }

    [Fact]
    public async Task AttachAuthentication_UsesConfiguredAdminKeyWhenBearerIsNotForwardable()
    {
        var sessions = new InMemoryConsoleAccountSessionStore();
        await sessions.SaveSessionAsync(new ConsoleAccountSession
        {
            ProfileId = "env-a",
            AccessToken = ConsoleAuthConstants.SessionSentinelPrefix + "env-a"
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://server.example/approve");

        await ConsoleServerHttp.AttachAuthenticationAsync(
            request,
            sessions,
            Profile("env-a", ConsoleAccountAuthMode.AccountRbac),
            "shared-admin-key",
            CancellationToken.None);

        Assert.Null(request.Headers.Authorization);
        Assert.True(
            request.Headers.TryGetValues("X-API-Key", out var values)
            && values.Single() == "shared-admin-key");
    }

    [Fact]
    public async Task AttachAuthentication_LeavesRequestUnauthenticatedWhenNoCredentialExists()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://server.example/approve");

        await ConsoleServerHttp.AttachAuthenticationAsync(
            request,
            new InMemoryConsoleAccountSessionStore(),
            Profile("env-a", ConsoleAccountAuthMode.AccountRbac),
            adminApiKey: null,
            CancellationToken.None);

        Assert.Null(request.Headers.Authorization);
        Assert.False(request.Headers.Contains("X-API-Key"));
    }

    [Fact]
    public async Task ResolveForwardableBearer_ReturnsRealOperatorBearer()
    {
        var sessions = new InMemoryConsoleAccountSessionStore();
        await sessions.SaveSessionAsync(new ConsoleAccountSession
        {
            ProfileId = "env-a",
            AccessToken = "real-operator-bearer"
        });

        var token = await ConsoleServerHttp.ResolveForwardableBearerAsync(
            sessions,
            Profile("env-a", ConsoleAccountAuthMode.AccountRbac),
            CancellationToken.None);

        Assert.Equal("real-operator-bearer", token);
    }

    [Fact]
    public async Task ResolveForwardableBearer_DropsSessionSentinel()
    {
        var sessions = new InMemoryConsoleAccountSessionStore();
        await sessions.SaveSessionAsync(new ConsoleAccountSession
        {
            ProfileId = "env-a",
            AccessToken = ConsoleAuthConstants.SessionSentinelPrefix + "env-a"
        });

        var token = await ConsoleServerHttp.ResolveForwardableBearerAsync(
            sessions,
            Profile("env-a", ConsoleAccountAuthMode.AccountRbac),
            CancellationToken.None);

        Assert.Null(token);
    }

    [Fact]
    public async Task ResolveForwardableBearer_ReturnsNullForAnonymousProfile()
    {
        var sessions = new InMemoryConsoleAccountSessionStore();
        await sessions.SaveSessionAsync(new ConsoleAccountSession
        {
            ProfileId = "env-a",
            AccessToken = "real-operator-bearer"
        });

        var token = await ConsoleServerHttp.ResolveForwardableBearerAsync(
            sessions,
            Profile("env-a", ConsoleAccountAuthMode.Anonymous),
            CancellationToken.None);

        Assert.Null(token);
    }

    [Theory]
    [InlineData("https://server.example", "api/v1/version", "https://server.example/api/v1/version")]
    [InlineData("https://server.example/", "api/v1/version", "https://server.example/api/v1/version")]
    [InlineData("https://server.example/honua", "api/v1/version", "https://server.example/honua/api/v1/version")]
    public void BuildUri_PreservesBasePathWithTrailingSlash(string baseUri, string relative, string expected)
    {
        var result = ConsoleServerHttp.BuildUri(new Uri(baseUri), relative);

        Assert.Equal(expected, result.AbsoluteUri);
    }

    private static ConsoleEnvironmentProfile Profile(string id, ConsoleAccountAuthMode mode) =>
        new()
        {
            Id = id,
            DisplayName = id,
            ServerBaseUri = new Uri("https://server.example"),
            Account = new ConsoleAccountBinding { AuthMode = mode, AccountId = "op" }
        };
}
