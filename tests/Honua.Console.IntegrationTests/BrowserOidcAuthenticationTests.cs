using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Honua.Console.Shell.Services;
using Honua.Console.Web.Runtime;
using Microsoft.AspNetCore.Components;

namespace Honua.Console.IntegrationTests;

public sealed class BrowserOidcAuthenticationTests
{
    [Fact]
    public async Task BeginSendsMountedReturnPathAndAnonymousCsrfToServerBroker()
    {
        using var fixture = new BrokerFixture();
        var destination = await fixture.Authentication.BeginAsync("entra", "/operate/connections");
        Assert.Equal("https://identity.example/authorize?state=issued-state", destination.AbsoluteUri);
        Assert.Equal("/console/operate/connections", fixture.Handler.ReturnTo);
        Assert.Equal(1, fixture.Handler.Authorizations);
        Assert.False(fixture.Session.IsAuthenticated);
        Assert.Null(fixture.Session.AccessToken);
    }

    [Fact]
    public async Task LandingAfterServerCallbackLoadsIdentityAndFreshCsrf()
    {
        using var fixture = new BrokerFixture();
        fixture.Handler.CallbackCompleted = true;
        await fixture.Provider.RefreshAsync();
        Assert.True(fixture.Session.IsAuthenticated);
        Assert.Equal("identified-csrf", fixture.Session.CsrfToken);
        Assert.Null(fixture.Session.AccessToken);
        Assert.Equal(0, fixture.Handler.Authorizations);
    }

    [Fact]
    public async Task UnknownProviderCannotStartAnAuthorizationRequest()
    {
        using var fixture = new BrokerFixture();
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Authentication.BeginAsync("unknown", "/"));
        Assert.Equal(0, fixture.Handler.Authorizations);
    }

    [Theory]
    [InlineData("//other.example/")]
    [InlineData("/../outside")]
    [InlineData("/operate/../../outside")]
    public async Task InvalidReturnPathCannotStartAnAuthorizationRequest(string returnTo)
    {
        using var fixture = new BrokerFixture();
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Authentication.BeginAsync("entra", returnTo));
        Assert.Equal(0, fixture.Handler.Authorizations);
    }

    [Fact]
    public async Task SeparateOriginCannotUseCookieBroker()
    {
        using var fixture = new BrokerFixture(crossOrigin: true);
        Assert.False(fixture.Authentication.IsAvailable);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Authentication.BeginAsync("entra", "/"));
        Assert.Equal(0, fixture.Handler.Authorizations);
    }

    private sealed class BrokerFixture : IDisposable
    {
        public ConsoleBrowserSession Session { get; } = new();
        public BrokerHandler Handler { get; } = new();
        private readonly HttpClient _client;
        public BrowserOidcAuthentication Authentication { get; }
        public BrowserAuthenticationStateProvider Provider { get; }

        public BrokerFixture(bool crossOrigin = false)
        {
            var origin = new Uri("https://honua.example/");
            _client = new HttpClient(new BrowserApiHandler(new BrowserApiOrigins(origin, origin), Session, BrowserApiAccess.Public)
            { InnerHandler = Handler })
            { BaseAddress = origin };
            var api = new ConsoleApiClient(_client) { SupportsPasswordSignIn = false, UsesCookieSession = !crossOrigin };
            Provider = new BrowserAuthenticationStateProvider(api, Session, new InMemoryConsoleAccountSessionStore());
            Authentication = new BrowserOidcAuthentication(api, Provider, new MountedNavigation());
        }

        public void Dispose() => _client.Dispose();
    }

    private sealed class MountedNavigation : NavigationManager
    {
        public MountedNavigation() => Initialize("https://honua.example/console/", "https://honua.example/console/auth/signin");
        protected override void NavigateToCore(string uri, bool forceLoad) => throw new NotSupportedException();
    }

    private sealed class BrokerHandler : HttpMessageHandler
    {
        public int Authorizations { get; private set; }
        public string? ReturnTo { get; private set; }
        public bool CallbackCompleted { get; set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            Assert.Null(request.Headers.Authorization);
            if (request.Method == HttpMethod.Post)
            {
                Assert.EndsWith("/providers/entra/authorize-url", path);
                Assert.Equal("anonymous-csrf", Assert.Single(request.Headers.GetValues("X-Honua-CSRF")));
                using var body = await request.Content!.ReadFromJsonAsync<JsonDocument>(cancellationToken);
                ReturnTo = body!.RootElement.GetProperty("returnTo").GetString();
                Assert.Single(body.RootElement.EnumerateObject());
                Authorizations++;
                return Json(new { authorizeUrl = "https://identity.example/authorize?state=issued-state" });
            }
            if (path.EndsWith("/csrf", StringComparison.Ordinal))
            {
                return Json(new { requestToken = CallbackCompleted ? "identified-csrf" : "anonymous-csrf" });
            }
            if (path.EndsWith("/config", StringComparison.Ordinal))
            {
                return Json(new { oidcEnabled = true, providers = new[] { new { key = "entra", displayName = "Organization" } } });
            }
            Assert.EndsWith("/session", path);
            return Json(new { isAuthenticated = CallbackCompleted, claims = new[] { new { type = ClaimTypes.NameIdentifier, value = "operator" } }, expiresAt = DateTimeOffset.UtcNow.AddMinutes(5) });
        }
        private static HttpResponseMessage Json(object body) => new(HttpStatusCode.OK) { Content = JsonContent.Create(body) };
    }
}
