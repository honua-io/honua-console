using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Honua.Console.Shell.Services;
using Honua.Console.Web.Runtime;

namespace Honua.Console.IntegrationTests;

public sealed class BrowserApiTransportTests
{
    private static readonly Uri Api = new("https://honua.example/api-root/");
    private static readonly Uri App = new("https://honua.example/console/");

    [Theory]
    [InlineData("https://other.example/api-root/layers")]
    [InlineData("https://honua.example/another-root/layers")]
    [InlineData("https://honua.example/api-root/layers#fragment")]
    [InlineData("https://user@honua.example/api-root/layers")]
    public async Task RejectsRequestsOutsideConfiguredApi(string target)
    {
        var recorder = new RecordingHandler();
        using var client = Client(new ConsoleBrowserSession(), recorder, new BrowserTransportOptions(new BrowserApiOrigins(Api, App), BrowserApiAccess.Public));
        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync(target));
        Assert.Equal(0, recorder.Calls);
    }

    [Fact]
    public async Task PrivilegedReadsRequireAnUnexpiredSession()
    {
        var recorder = new RecordingHandler();
        var session = new ConsoleBrowserSession();
        session.Set(Principal(), new ConsoleSessionCredentials(ExpiresAt: DateTimeOffset.UtcNow.AddMinutes(-1)));
        using var client = Client(session, recorder);
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync("layers"));
        Assert.Equal(HttpStatusCode.Unauthorized, error.StatusCode);
        Assert.Equal(0, recorder.Calls);
    }

    [Fact]
    public async Task SameOriginMutationsRequireIssuedCsrfTokenAndStripCallerCredentials()
    {
        var recorder = new RecordingHandler();
        var session = new ConsoleBrowserSession();
        session.Set(Principal());
        using var client = Client(session, recorder);
        await Assert.ThrowsAsync<HttpRequestException>(() => client.PostAsync("layers", null));
        Assert.Equal(0, recorder.Calls);

        session.Set(Principal(), new ConsoleSessionCredentials(CsrfToken: "issued-csrf"));
        using var request = new HttpRequestMessage(HttpMethod.Post, "layers");
        request.Headers.Add("X-API-Key", "must-not-leave");
        request.Headers.Add("Cookie", "must-not-leave");
        request.Headers.Add("X-CSRF-Token", "caller-token");
        request.Headers.Add("X-Honua-CSRF", "caller-token");
        request.Headers.Authorization = new("Bearer", "caller-token");
        using var response = await client.SendAsync(request);
        Assert.False(request.Headers.Contains("X-API-Key"));
        Assert.False(request.Headers.Contains("Cookie"));
        Assert.Null(request.Headers.Authorization);
        Assert.False(request.Headers.Contains("X-CSRF-Token"));
        Assert.Equal("issued-csrf", Assert.Single(request.Headers.GetValues("X-Honua-CSRF")));
        Assert.Equal(1, recorder.Calls);
    }

    [Fact]
    public async Task CrossOriginRequestsRequireAndUseTheInMemoryOperatorToken()
    {
        var recorder = new RecordingHandler();
        var session = new ConsoleBrowserSession();
        session.Set(Principal());
        using var client = Client(session, recorder, new BrowserApiOrigins(Api, new Uri("https://console.example/console/")));
        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync("layers"));
        session.Set(Principal(), new ConsoleSessionCredentials(AccessToken: "operator-token", ExpiresAt: DateTimeOffset.UtcNow.AddMinutes(5)));
        using var request = new HttpRequestMessage(HttpMethod.Post, "layers");
        using var response = await client.SendAsync(request);
        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
        Assert.Equal("operator-token", request.Headers.Authorization?.Parameter);
        Assert.False(request.Headers.Contains("X-CSRF-Token"));
        Assert.Equal(1, recorder.Calls);
    }

    [Fact]
    public void RuntimeConfigResolvesSameOriginApiOutsideTheConsolePrefix()
    {
        Assert.Equal(new Uri("https://honua.example/"), new ConsoleRuntimeConfiguration().ResolveApiBaseUri(App));
    }

    [Theory]
    [InlineData("{}", true)]
    [InlineData("{\"apiBaseUrl\":\"/\"}", true)]
    [InlineData("{\"v4Enabled\":true}", true)]
    [InlineData("{\"v4Enabled\":false}", false)]
    public void RuntimeConfigEnablesV4UnlessExplicitlyRolledBack(string json, bool expected)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Assert.Equal(expected, JsonSerializer.Deserialize<ConsoleRuntimeConfiguration>(json, options)!.V4Enabled);
    }

    [Fact]
    public void SessionChangePublishesTheCurrentSnapshot()
    {
        var session = new ConsoleBrowserSession();
        ConsoleBrowserSessionSnapshot? published = null;
        session.Changed += snapshot => published = snapshot;
        session.Set(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "operator")], "test")),
            new ConsoleSessionCredentials(AccessToken: "token"));
        Assert.Equal("operator", published?.PrincipalId);
        Assert.Equal("token", published?.AccessToken);
        session.Clear();
        Assert.False(published?.Authenticated);
        Assert.Null(published?.AccessToken);
    }

    [Fact]
    public void ExpiryPublishesANewRevision()
    {
        var session = new ConsoleBrowserSession();
        ConsoleBrowserSessionSnapshot? published = null;
        session.Changed += snapshot => published = snapshot;
        session.Set(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "operator")], "test")),
            new ConsoleSessionCredentials(AccessToken: "token", ExpiresAt: DateTimeOffset.UtcNow.AddMilliseconds(40)));
        Assert.True(published?.Authenticated);
        var revision = published!.Revision;
        Thread.Sleep(60);
        var expired = session.Snapshot();
        Assert.False(expired.Authenticated);
        Assert.Null(expired.AccessToken);
        Assert.True(expired.Revision > revision);
        Assert.Equal(expired.Revision, published?.Revision);
        Assert.Equal(expired.Revision, session.Snapshot().Revision);
    }

    [Theory]
    [InlineData("http://remote.example/")]
    [InlineData("https://user:password@honua.example/")]
    [InlineData("//other.example/")]
    [InlineData("https://honua.example/?key=value")]
    public void RuntimeConfigRejectsUnsafeApiAddresses(string address)
    {
        Assert.Throws<InvalidOperationException>(() => new ConsoleRuntimeConfiguration { ApiBaseUrl = address }.ResolveApiBaseUri(App));
    }

    [Fact]
    public void RuntimeConfigRejectsCredentialsAndNullAuthenticationConfiguration()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ConsoleRuntimeConfiguration>("{\"adminApiKey\":\"secret\"}"));
        Assert.Throws<InvalidOperationException>(() => new ConsoleRuntimeConfiguration { Auth = null! }.ResolveApiBaseUri(App));
        Assert.Throws<InvalidOperationException>(() => new ConsoleRuntimeConfiguration
        {
            Auth = new ConsoleBrowserAuthConfiguration { Mode = "oidc", Authority = "https://identity.example/", ClientId = "console", Scopes = null! }
        }.ResolveApiBaseUri(App));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BootstrapRetainsIssuedCredentialsAndRejectsExpiredIdentity(bool expired)
    {
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(expired ? -5 : 5);
        using var http = new HttpClient(new SessionHandler(expiresAt)) { BaseAddress = Api };
        var session = new ConsoleBrowserSession();
        var provider = new BrowserAuthenticationStateProvider(new ConsoleApiClient(http), session,
            new InMemoryConsoleAccountSessionStore());

        var state = await provider.GetAuthenticationStateAsync();

        Assert.Equal(!expired, state.User.Identity?.IsAuthenticated);
        Assert.Equal(!expired, session.IsAuthenticated);
        Assert.Equal("issued-csrf", session.CsrfToken);
        Assert.Null(session.AccessToken);
        Assert.Equal(expiresAt, session.ExpiresAt);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PasswordSignInUsesTheOriginContractAndRefreshesIdentity(bool crossOrigin)
    {
        var session = new ConsoleBrowserSession();
        var handler = new SignInHandler(crossOrigin);
        var origins = new BrowserApiOrigins(Api, crossOrigin ? new Uri("https://console.example/") : App);
        using var http = new HttpClient(new BrowserApiHandler(origins, session, BrowserApiAccess.Public) { InnerHandler = handler }) { BaseAddress = Api };
        var provider = new BrowserAuthenticationStateProvider(new ConsoleApiClient(http) { UsesCookieSession = !crossOrigin },
            session, new InMemoryConsoleAccountSessionStore());

        await provider.SignInAsync("fixture-password");

        Assert.True((await provider.GetAuthenticationStateAsync()).User.Identity?.IsAuthenticated);
        Assert.Equal(crossOrigin ? "issued-bearer" : null, session.AccessToken);
        Assert.True(session.ExpiresAt > DateTimeOffset.UtcNow);
        Assert.Equal(crossOrigin ? null : "identified-csrf", session.CsrfToken);
        Assert.Equal(crossOrigin ? new[] { "password-token", "session" } : new[] { "csrf", "session", "session", "csrf" }, handler.Paths);
    }

    [Fact]
    public async Task RejectedPasswordClearsPriorCredentialsAndIdentity()
    {
        var session = new ConsoleBrowserSession();
        session.Set(Principal(), new ConsoleSessionCredentials(AccessToken: "old-token"));
        using var http = new HttpClient(new DeniedSignInHandler()) { BaseAddress = Api };
        var store = new InMemoryConsoleAccountSessionStore();
        await store.SaveSessionAsync(new Honua.Console.Shell.Models.ConsoleAccountSession { ProfileId = "configured-api" });
        var provider = new BrowserAuthenticationStateProvider(new ConsoleApiClient(http) { UsesCookieSession = false }, session, store);

        await Assert.ThrowsAsync<HttpRequestException>(() => provider.SignInAsync("rejected"));

        Assert.False((await provider.GetAuthenticationStateAsync()).User.Identity?.IsAuthenticated);
        Assert.Null(session.AccessToken);
        Assert.Null(session.CsrfToken);
        Assert.Null(await store.GetSessionAsync("configured-api"));
    }

    [Fact]
    public async Task CancelledSignInClearsThePriorStoredSession()
    {
        var session = new ConsoleBrowserSession();
        session.Set(Principal(), new ConsoleSessionCredentials(AccessToken: "old-token"));
        using var http = new HttpClient(new DeniedSignInHandler()) { BaseAddress = Api };
        var store = new InMemoryConsoleAccountSessionStore();
        await store.SaveSessionAsync(new Honua.Console.Shell.Models.ConsoleAccountSession { ProfileId = "configured-api" });
        var provider = new BrowserAuthenticationStateProvider(new ConsoleApiClient(http) { UsesCookieSession = false }, session, store);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.SignInAsync("password", cancellation.Token));

        Assert.False((await provider.GetAuthenticationStateAsync()).User.Identity?.IsAuthenticated);
        Assert.Null(session.AccessToken);
        Assert.Null(await store.GetSessionAsync("configured-api"));
    }

    private sealed class DeniedSignInHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
    }

    private sealed class SignInHandler(bool crossOrigin) : HttpMessageHandler
    {
        private bool _signedIn;
        public List<string> Paths { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.Segments.Last();
            Paths.Add(path);
            if (request.Method == HttpMethod.Post)
            {
                Assert.Equal(crossOrigin ? "password-token" : "session", path);
                using var body = await request.Content!.ReadFromJsonAsync<JsonDocument>(cancellationToken);
                Assert.Equal("fixture-password", body!.RootElement.GetProperty("password").GetString());
                Assert.Null(request.Headers.Authorization);
                if (!crossOrigin)
                {
                    Assert.Equal("anonymous-csrf", Assert.Single(request.Headers.GetValues("X-Honua-CSRF")));
                }
                _signedIn = true;
                return crossOrigin ? Json(new { accessToken = "issued-bearer", tokenType = "Bearer", expiresAt = DateTimeOffset.UtcNow.AddMinutes(5) })
                    : new HttpResponseMessage(HttpStatusCode.NoContent);
            }
            if (path == "csrf")
            {
                Assert.False(crossOrigin);
                return Json(new { requestToken = _signedIn ? "identified-csrf" : "anonymous-csrf" });
            }
            Assert.True(_signedIn);
            Assert.Equal(crossOrigin ? "issued-bearer" : null, request.Headers.Authorization?.Parameter);
            return Json(new { isAuthenticated = true, claims = new[] { new { type = ClaimTypes.NameIdentifier, value = "operator" } }, expiresAt = crossOrigin ? (DateTimeOffset?)null : DateTimeOffset.UtcNow.AddMinutes(5) });
        }

        private static HttpResponseMessage Json(object body) => new(HttpStatusCode.OK) { Content = JsonContent.Create(body) };
    }

    private sealed class SessionHandler(DateTimeOffset expiresAt) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/csrf", StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new { requestToken = "issued-csrf" })
                });
            }
            Assert.EndsWith("api/v1/admin/auth/session", request.RequestUri.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    isAuthenticated = true,
                    claims = new[] { new { type = ClaimTypes.NameIdentifier, value = "operator" } },
                    csrfToken = "issued-csrf",
                    accessToken = "issued-bearer",
                    expiresAt
                })
            });
        }
    }

    private static ClaimsPrincipal Principal() => new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "operator")], "test"));

    private static HttpClient Client(ConsoleBrowserSession session, RecordingHandler recorder, BrowserApiOrigins? origins = null) =>
        Client(session, recorder, new BrowserTransportOptions(origins ?? new BrowserApiOrigins(Api, App), BrowserApiAccess.Authenticated));

    private static HttpClient Client(ConsoleBrowserSession session, RecordingHandler recorder, BrowserTransportOptions options) => new(
        new BrowserApiHandler(options.Origins, session, options.Access) { InnerHandler = recorder })
    { BaseAddress = Api };

    private sealed record BrowserTransportOptions(BrowserApiOrigins Origins, BrowserApiAccess Access);

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
