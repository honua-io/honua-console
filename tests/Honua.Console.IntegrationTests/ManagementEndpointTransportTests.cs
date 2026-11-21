using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Honua.Console.Shell.Services;
using Honua.Console.Web.Runtime;

namespace Honua.Console.IntegrationTests;

public sealed class ManagementEndpointTransportTests
{
    [Fact]
    public async Task ManagementRequestsUseTheConfiguredEndpointAndOperatorBearer()
    {
        var recorder = new BrowserApiTransportTests.RecordingHandler();
        var management = new Uri("http://127.0.0.1:18082/management/");
        var origins = new BrowserApiOrigins(BrowserApiTransportTests.Api, new Uri("https://console.example/console/"), management);
        var session = new ConsoleBrowserSession();
        session.Set(BrowserApiTransportTests.Principal(), new ConsoleSessionCredentials(CsrfToken: "serving-csrf"));
        using var client = BrowserApiTransportTests.Client(session, recorder, new BrowserApiTransportTests.BrowserTransportOptions(origins, BrowserApiAccess.Manager));
        var missing = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync("layers?status=open"));
        Assert.Contains("management endpoint", missing.Message, StringComparison.Ordinal);

        session.Set(BrowserApiTransportTests.Principal(), new ConsoleSessionCredentials(AccessToken: "operator-token", ExpiresAt: DateTimeOffset.UtcNow.AddMinutes(5), CsrfToken: "serving-csrf"));
        using var response = await client.PostAsync("layers?status=open", null);
        Assert.Equal(new Uri("http://127.0.0.1:18082/management/layers?status=open"), recorder.Last);
        Assert.Equal("operator-token", recorder.Authorization);
        Assert.False(recorder.SentCsrf);
        Assert.Equal(1, recorder.Calls);
    }

    [Fact]
    public async Task SameOriginManagerUsesTheOperatorBearer()
    {
        var recorder = new BrowserApiTransportTests.RecordingHandler();
        var app = new Uri("https://honua.example/console/");
        var api = new Uri("https://api.example/");
        var management = new Uri("https://honua.example/manager/");
        var origins = new BrowserApiOrigins(api, app, management);
        var session = new ConsoleBrowserSession();
        session.Set(BrowserApiTransportTests.Principal(), new ConsoleSessionCredentials(AccessToken: "operator-token", ExpiresAt: DateTimeOffset.UtcNow.AddMinutes(5)));
        using var client = new HttpClient(new BrowserApiHandler(origins, session, BrowserApiAccess.Manager) { InnerHandler = recorder })
        {
            BaseAddress = api
        };

        using var response = await client.PostAsync("layers?status=open", null);

        Assert.Equal(new Uri("https://honua.example/manager/layers?status=open"), recorder.Last);
        Assert.Equal("operator-token", recorder.Authorization);
        Assert.False(recorder.SentCsrf);
    }

    [Fact]
    public async Task ManagerOnTheServingOriginKeepsTheCookie()
    {
        var recorder = new BrowserApiTransportTests.RecordingHandler();
        var api = new Uri("https://honua.example/api/");
        var app = new Uri("https://honua.example/console/");
        var management = new Uri("https://honua.example/manager/");
        var origins = new BrowserApiOrigins(api, app, management);
        var session = new ConsoleBrowserSession();
        session.Set(BrowserApiTransportTests.Principal(), new ConsoleSessionCredentials(CsrfToken: "issued-csrf"));
        using var client = new HttpClient(new BrowserApiHandler(origins, session, BrowserApiAccess.Manager) { InnerHandler = recorder })
        {
            BaseAddress = api
        };

        using var response = await client.PostAsync("layers", null);

        Assert.Equal(new Uri(management, "layers"), recorder.Last);
        Assert.Null(recorder.Authorization);
        Assert.True(recorder.SentCsrf);
    }

    [Fact]
    public async Task OmittedManagerStaysOnTheServingCookie()
    {
        var recorder = new BrowserApiTransportTests.RecordingHandler();
        var origins = new BrowserApiOrigins(BrowserApiTransportTests.Api, BrowserApiTransportTests.App);
        var session = new ConsoleBrowserSession();
        session.Set(BrowserApiTransportTests.Principal(), new ConsoleSessionCredentials(CsrfToken: "issued-csrf"));
        using var client = new HttpClient(new BrowserApiHandler(origins, session, BrowserApiAccess.Manager) { InnerHandler = recorder })
        {
            BaseAddress = BrowserApiTransportTests.Api
        };

        using var response = await client.PostAsync("layers", null);

        Assert.Equal(new Uri(BrowserApiTransportTests.Api, "layers"), recorder.Last);
        Assert.Null(recorder.Authorization);
        Assert.True(recorder.SentCsrf);
    }

    [Fact]
    public async Task ServingRequestsKeepCookieAuthWhenTheManagerSharesTheOrigin()
    {
        var recorder = new BrowserApiTransportTests.RecordingHandler();
        var api = new Uri("https://honua.example/api/");
        var app = new Uri("https://honua.example/console/");
        var management = new Uri("https://honua.example/manager/");
        var origins = new BrowserApiOrigins(api, app, management);
        var session = new ConsoleBrowserSession();
        session.Set(BrowserApiTransportTests.Principal(), new ConsoleSessionCredentials(
            CsrfToken: "issued-csrf", AccessToken: "operator-token", ExpiresAt: DateTimeOffset.UtcNow.AddMinutes(5)));
        session.RememberManagementBearer("manager-token", DateTimeOffset.UtcNow.AddMinutes(5));
        using var client = new HttpClient(new BrowserApiHandler(origins, session, BrowserApiAccess.Authenticated) { InnerHandler = recorder })
        {
            BaseAddress = api
        };

        using var response = await client.PostAsync("layers", null);

        Assert.Equal(new Uri(api, "layers"), recorder.Last);
        Assert.Null(recorder.Authorization);
        Assert.True(recorder.SentCsrf);
    }

    [Fact]
    public async Task ServingRequestsStayOffTheManagementEndpoint()
    {
        var recorder = new BrowserApiTransportTests.RecordingHandler();
        var origins = new BrowserApiOrigins(BrowserApiTransportTests.Api, BrowserApiTransportTests.App, new Uri("http://127.0.0.1:18082/"));
        var session = new ConsoleBrowserSession();
        session.Set(BrowserApiTransportTests.Principal(), new ConsoleSessionCredentials(CsrfToken: "issued-csrf"));
        session.RememberManagementBearer("manager-token", DateTimeOffset.UtcNow.AddMinutes(5));
        using var client = BrowserApiTransportTests.Client(session, recorder, new BrowserApiTransportTests.BrowserTransportOptions(origins, BrowserApiAccess.Authenticated));
        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync(new Uri("http://127.0.0.1:18082/layers")));
        using var response = await client.PostAsync("layers", null);
        Assert.Equal(new Uri(BrowserApiTransportTests.Api, "layers"), recorder.Last);
        Assert.Null(recorder.Authorization);
        Assert.True(recorder.SentCsrf);
        Assert.Equal(1, recorder.Calls);
    }

    [Fact]
    public async Task CookieSignInRequestsTheManagerBearerWithoutBrowserCredentials()
    {
        var session = new ConsoleBrowserSession();
        var handler = new ManagerBearerSignInHandler();
        var provider = SignInProvider(session, handler);

        await provider.SignInAsync("fixture-password");

        Assert.True((await provider.GetAuthenticationStateAsync()).User.Identity?.IsAuthenticated);
        Assert.Null(session.AccessToken);
        Assert.Equal("issued-bearer", session.ManagementAccessToken);
        Assert.Null(session.ManagementUnavailableReason);
        Assert.Equal("identified-csrf", session.CsrfToken);
        Assert.Equal(["csrf", "session", "password-token", "session", "csrf"], handler.Paths);
        Assert.Null(handler.TokenCsrf);
        Assert.Equal("omit", handler.TokenCredentials);
        Assert.Equal(0, handler.Logouts);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task ServingSignInSurvivesAManagerBearerTheServerDoesNotIssue(HttpStatusCode status)
    {
        var session = new ConsoleBrowserSession();
        var handler = new ManagerBearerSignInHandler { TokenStatus = status };
        var provider = SignInProvider(session, handler);

        await provider.SignInAsync("fixture-password");

        Assert.True((await provider.GetAuthenticationStateAsync()).User.Identity?.IsAuthenticated);
        Assert.Null(session.ManagementAccessToken);
        Assert.Equal(0, handler.Logouts);
        var origins = new BrowserApiOrigins(BrowserApiTransportTests.Api, BrowserApiTransportTests.App, new Uri("http://127.0.0.1:18082/"));
        using var manager = BrowserApiTransportTests.Client(session, new BrowserApiTransportTests.RecordingHandler(),
            new BrowserApiTransportTests.BrowserTransportOptions(origins, BrowserApiAccess.Manager));
        var refused = await Assert.ThrowsAsync<HttpRequestException>(() => manager.GetAsync("api/v1/admin/proposals"));
        Assert.Contains($"HTTP {(int)status}", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SignInThatFailsAfterTheCookieStepEndsTheServerSession()
    {
        var session = new ConsoleBrowserSession();
        var handler = new ManagerBearerSignInHandler { SessionEstablished = false };
        var provider = SignInProvider(session, handler);

        await Assert.ThrowsAsync<HttpRequestException>(() => provider.SignInAsync("fixture-password"));

        Assert.False(session.IsAuthenticated);
        Assert.Equal(1, handler.Logouts);
        Assert.Equal("identified-csrf", handler.LogoutCsrf);
        Assert.Equal("same-origin", handler.LogoutCredentials);
        Assert.Null(session.ManagementAccessToken);
    }

    [Fact]
    public async Task ALogoutTimeoutKeepsTheOriginalSignInError()
    {
        var session = new ConsoleBrowserSession();
        var handler = new ManagerBearerSignInHandler { SessionEstablished = false, LogoutTimesOut = true };
        var provider = SignInProvider(session, handler);

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => provider.SignInAsync("fixture-password"));

        Assert.Equal(HttpStatusCode.Unauthorized, error.StatusCode);
        Assert.False(session.IsAuthenticated);
        Assert.Null(session.ManagementAccessToken);
        Assert.Equal(1, handler.Logouts);
    }

    private static BrowserAuthenticationStateProvider SignInProvider(ConsoleBrowserSession session, ManagerBearerSignInHandler handler)
    {
        var http = new HttpClient(new BrowserApiHandler(new BrowserApiOrigins(BrowserApiTransportTests.Api, BrowserApiTransportTests.App), session, BrowserApiAccess.Public) { InnerHandler = handler })
        {
            BaseAddress = BrowserApiTransportTests.Api
        };
        return new BrowserAuthenticationStateProvider(
            new ConsoleApiClient(http) { UsesCookieSession = true, CapturesManagementBearer = true },
            session, new InMemoryConsoleAccountSessionStore());
    }

    private sealed class ManagerBearerSignInHandler : HttpMessageHandler
    {
        private bool _signedIn;
        public List<string> Paths { get; } = [];
        public HttpStatusCode TokenStatus { get; init; } = HttpStatusCode.OK;
        public bool SessionEstablished { get; init; } = true;
        public bool LogoutTimesOut { get; init; }
        public string? TokenCsrf { get; private set; }
        public string? TokenCredentials { get; private set; }
        public int Logouts { get; private set; }
        public string? LogoutCsrf { get; private set; }
        public string? LogoutCredentials { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.Segments.Last();
            Paths.Add(path);
            Assert.Null(request.Headers.Authorization);
            if (path == "logout") return RecordLogout(request);
            if (request.Method == HttpMethod.Post) return await SignInAsync(request, path, cancellationToken);
            if (path == "csrf") return Json(new { requestToken = _signedIn ? "identified-csrf" : "anonymous-csrf" });
            Assert.True(_signedIn);
            return Json(new { isAuthenticated = SessionEstablished, claims = new[] { new { type = ClaimTypes.NameIdentifier, value = "operator" } }, expiresAt = DateTimeOffset.UtcNow.AddMinutes(5) });
        }

        private async Task<HttpResponseMessage> SignInAsync(HttpRequestMessage request, string path, CancellationToken cancellationToken)
        {
            using var body = await request.Content!.ReadFromJsonAsync<JsonDocument>(cancellationToken);
            Assert.Equal("fixture-password", body!.RootElement.GetProperty("password").GetString());
            if (path == "session")
            {
                Assert.False(_signedIn);
                Assert.Equal("anonymous-csrf", Assert.Single(request.Headers.GetValues("X-Honua-CSRF")));
                _signedIn = true;
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            Assert.Equal("password-token", path);
            Assert.True(_signedIn);
            TokenCsrf = request.Headers.TryGetValues("X-Honua-CSRF", out var csrf) ? csrf.Single() : null;
            TokenCredentials = Credentials(request);
            return TokenStatus == HttpStatusCode.OK
                ? Json(new { accessToken = "issued-bearer", tokenType = "Bearer", expiresAt = DateTimeOffset.UtcNow.AddMinutes(5) })
                : new HttpResponseMessage(TokenStatus);
        }

        private HttpResponseMessage RecordLogout(HttpRequestMessage request)
        {
            Logouts++;
            LogoutCsrf = request.Headers.GetValues("X-Honua-CSRF").Single();
            LogoutCredentials = Credentials(request);
            if (LogoutTimesOut) throw new TaskCanceledException("The logout request timed out.");
            _signedIn = false;
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }

        private static string? Credentials(HttpRequestMessage request) =>
            request.Options.TryGetValue(new HttpRequestOptionsKey<IDictionary<string, object>>("WebAssemblyFetchOptions"), out var options)
                && options.TryGetValue("credentials", out var credentials) ? credentials as string : null;

        private static HttpResponseMessage Json(object body) => new(HttpStatusCode.OK) { Content = JsonContent.Create(body) };
    }
}
