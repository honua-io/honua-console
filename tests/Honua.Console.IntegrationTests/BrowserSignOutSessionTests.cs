using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;
using Honua.Console.Web.Runtime;

namespace Honua.Console.IntegrationTests;

public sealed class BrowserSignOutSessionTests
{
    private static readonly Uri Api = new("https://honua.example/api-root/");

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SignOutClearsCredentialsStoredAccountAndCapturedAuthority(bool cookieSession)
    {
        var session = AuthenticatedSession();
        var store = await AccountStore();
        var capture = await store.CaptureSessionAsync("configured-api");
        var authority = session.CaptureAuthority(Api);
        var handler = new LogoutHandler();
        using var http = CreateClient(session, handler);
        using var provider = new BrowserAuthenticationStateProvider(
            new ConsoleApiClient(http) { UsesCookieSession = cookieSession }, session, store);
        var notifications = 0;
        provider.AuthenticationStateChanged += _ => notifications++;

        await provider.SignOutAsync();

        Assert.False(session.IsAuthenticated);
        Assert.Null(session.AccessToken);
        Assert.Null(session.ManagementAccessToken);
        Assert.Null(session.ManagementExpiresAt);
        Assert.Null(session.CsrfToken);
        Assert.Null(session.ExpiresAt);
        Assert.NotNull(authority);
        Assert.Null(session.CaptureAuthority(Api));
        Assert.Null(await store.GetSessionAsync("configured-api"));
        Assert.False(await store.IsCurrentSessionAsync(capture));
        Assert.False((await provider.GetAuthenticationStateAsync()).User.Identity!.IsAuthenticated);
        Assert.Equal(1, notifications);
        Assert.Equal(cookieSession ? new[] { "GET /api-root/api/v1/admin/auth/csrf", "POST /api-root/api/v1/admin/auth/logout" } : [], handler.Requests);
        using var privileged = new HttpClient(new BrowserApiHandler(
            new BrowserApiOrigins(Api, Api), session, BrowserApiAccess.Authenticated) { InnerHandler = handler }) { BaseAddress = Api };
        var denied = await Assert.ThrowsAsync<HttpRequestException>(() => privileged.GetAsync("api/v1/admin/services"));
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FailedServerLogoutRetainsTheAccountAndCredentialsForRetry(bool csrfFailure)
    {
        var session = AuthenticatedSession();
        var store = await AccountStore();
        var handler = new LogoutHandler { FailCsrf = csrfFailure, FailLogout = !csrfFailure };
        using var http = CreateClient(session, handler);
        using var provider = new BrowserAuthenticationStateProvider(new ConsoleApiClient(http), session, store);

        await Assert.ThrowsAsync<HttpRequestException>(() => provider.SignOutAsync());

        Assert.True(session.IsAuthenticated);
        Assert.Equal("operator-token", session.AccessToken);
        Assert.Equal("manager-token", session.ManagementAccessToken);
        Assert.NotNull(await store.GetSessionAsync("configured-api"));
        Assert.Equal(csrfFailure ? 1 : 2, handler.Requests.Count);
        handler.FailCsrf = false;
        handler.FailLogout = false;
        await provider.SignOutAsync();
        Assert.False(session.IsAuthenticated);
        Assert.Null(await store.GetSessionAsync("configured-api"));
    }

    [Fact]
    public async Task ASessionReadStartedBeforeLogoutCannotRestoreThePreviousOperator()
    {
        var session = AuthenticatedSession();
        var store = await AccountStore();
        var handler = new LogoutHandler();
        using var http = CreateClient(session, handler);
        using var provider = new BrowserAuthenticationStateProvider(new ConsoleApiClient(http), session, store);
        var pendingRead = provider.GetAuthenticationStateAsync();

        await provider.SignOutAsync();
        handler.SessionResponse.SetResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                isAuthenticated = true,
                claims = new[] { new { type = ClaimTypes.Name, value = "Previous operator" } }
            })
        });
        var stale = await pendingRead;

        Assert.False(stale.User.Identity!.IsAuthenticated);
        Assert.False((await provider.GetAuthenticationStateAsync()).User.Identity!.IsAuthenticated);
        Assert.Null(await store.GetSessionAsync("configured-api"));
        Assert.False(session.IsAuthenticated);
    }

    private static ConsoleBrowserSession AuthenticatedSession()
    {
        var session = new ConsoleBrowserSession();
        session.Set(new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "alice"), new Claim(ClaimTypes.Name, "Alice Operator")
        }, "browser")), new ConsoleSessionCredentials("old-csrf", "operator-token", DateTimeOffset.UtcNow.AddMinutes(5)));
        session.RememberManagementBearer("manager-token", DateTimeOffset.UtcNow.AddMinutes(5));
        return session;
    }

    private static async Task<InMemoryConsoleAccountSessionStore> AccountStore()
    {
        var store = new InMemoryConsoleAccountSessionStore();
        await store.SaveSessionAsync(new ConsoleAccountSession { ProfileId = "configured-api", AccountId = "alice" });
        return store;
    }

    private static HttpClient CreateClient(ConsoleBrowserSession session, LogoutHandler handler) => new(
        new BrowserApiHandler(new BrowserApiOrigins(Api, Api), session, BrowserApiAccess.Public) { InnerHandler = handler }) { BaseAddress = Api };

    private sealed class LogoutHandler : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];
        public bool FailCsrf { get; set; }
        public bool FailLogout { get; set; }
        public TaskCompletionSource<HttpResponseMessage> SessionResponse { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add($"{request.Method} {request.RequestUri!.AbsolutePath}");
            if (request.RequestUri.AbsolutePath.EndsWith("/session", StringComparison.Ordinal)) return SessionResponse.Task;
            if (request.Method == HttpMethod.Get)
                return Task.FromResult(new HttpResponseMessage(FailCsrf ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new { requestToken = "logout-csrf" })
                });
            Assert.Equal("logout-csrf", Assert.Single(request.Headers.GetValues("X-Honua-CSRF")));
            Assert.Null(request.Headers.Authorization);
            return Task.FromResult(new HttpResponseMessage(FailLogout ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK));
        }
    }
}
