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
    public async Task CookieSignInRefreshesCsrfBeforeTheManagerBearer()
    {
        var session = new ConsoleBrowserSession();
        var handler = new ManagerBearerSignInHandler();
        using var http = new HttpClient(new BrowserApiHandler(new BrowserApiOrigins(BrowserApiTransportTests.Api, BrowserApiTransportTests.App), session, BrowserApiAccess.Public) { InnerHandler = handler })
        {
            BaseAddress = BrowserApiTransportTests.Api
        };
        var provider = new BrowserAuthenticationStateProvider(
            new ConsoleApiClient(http) { UsesCookieSession = true, CapturesManagementBearer = true },
            session, new InMemoryConsoleAccountSessionStore());

        await provider.SignInAsync("fixture-password");

        Assert.True((await provider.GetAuthenticationStateAsync()).User.Identity?.IsAuthenticated);
        Assert.Null(session.AccessToken);
        Assert.Equal("issued-bearer", session.ManagementAccessToken);
        Assert.Equal("identified-csrf", session.CsrfToken);
        Assert.Equal(["csrf", "session", "csrf", "password-token", "session", "csrf"], handler.Paths);
    }

    private sealed class ManagerBearerSignInHandler : HttpMessageHandler
    {
        private bool _signedIn;
        public List<string> Paths { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.Segments.Last();
            Paths.Add(path);
            if (request.Method == HttpMethod.Post)
            {
                using var body = await request.Content!.ReadFromJsonAsync<JsonDocument>(cancellationToken);
                Assert.Equal("fixture-password", body!.RootElement.GetProperty("password").GetString());
                Assert.Null(request.Headers.Authorization);
                var csrf = Assert.Single(request.Headers.GetValues("X-Honua-CSRF"));
                if (path == "session")
                {
                    Assert.False(_signedIn);
                    Assert.Equal("anonymous-csrf", csrf);
                    _signedIn = true;
                    return new HttpResponseMessage(HttpStatusCode.NoContent);
                }

                Assert.Equal("password-token", path);
                Assert.True(_signedIn);
                Assert.Equal("identified-csrf", csrf);
                return Json(new { accessToken = "issued-bearer", tokenType = "Bearer", expiresAt = DateTimeOffset.UtcNow.AddMinutes(5) });
            }

            if (path == "csrf") return Json(new { requestToken = _signedIn ? "identified-csrf" : "anonymous-csrf" });
            Assert.True(_signedIn);
            Assert.Null(request.Headers.Authorization);
            return Json(new { isAuthenticated = true, claims = new[] { new { type = ClaimTypes.NameIdentifier, value = "operator" } }, expiresAt = DateTimeOffset.UtcNow.AddMinutes(5) });
        }

        private static HttpResponseMessage Json(object body) => new(HttpStatusCode.OK) { Content = JsonContent.Create(body) };
    }
}
