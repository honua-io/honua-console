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
        request.Headers.Authorization = new("Bearer", "caller-token");
        using var response = await client.SendAsync(request);
        Assert.False(request.Headers.Contains("X-API-Key"));
        Assert.False(request.Headers.Contains("Cookie"));
        Assert.Null(request.Headers.Authorization);
        Assert.Equal("issued-csrf", Assert.Single(request.Headers.GetValues("X-CSRF-Token")));
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
        session.Set(Principal(), new ConsoleSessionCredentials(AccessToken: "operator-token"));
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
        Assert.Equal("issued-bearer", session.AccessToken);
        Assert.Equal(expiresAt, session.ExpiresAt);
    }

    private sealed class SessionHandler(DateTimeOffset expiresAt) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.EndsWith("api/v1/admin/auth/session", request.RequestUri!.AbsolutePath);
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
