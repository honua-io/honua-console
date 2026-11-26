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
        var state = await fixture.Provider.GetAuthenticationStateAsync();
        Assert.True(state.User.Identity?.IsAuthenticated);
        Assert.True(fixture.Session.IsAuthenticated);
        Assert.Equal("identified-csrf", fixture.Session.CsrfToken);
        Assert.Null(fixture.Session.AccessToken);
        Assert.Equal(0, fixture.Handler.Authorizations);
    }

    [Theory]
    [InlineData("/", "/console/")]
    [InlineData("/operate/connections", "/console/operate/connections")]
    [InlineData("/operate/settings", "/console/operate/settings")]
    [InlineData("/operate/services", "/console/operate/services")]
    [InlineData("/publish", "/console/publish")]
    [InlineData("/operate/health", "/console/operate/health")]
    [InlineData("/operate/connections/11111111-1111-1111-1111-111111111111", "/console/operate/connections/11111111-1111-1111-1111-111111111111")]
    public async Task BrokerReceivesMountedReturnDestination(string returnTo, string expected)
    {
        using var fixture = new BrokerFixture();
        await fixture.Authentication.BeginAsync("entra", returnTo);
        Assert.Equal(expected, fixture.Handler.ReturnTo);
        Assert.Equal(1, fixture.Handler.Authorizations);
    }

    [Theory]
    [InlineData("/auth")]
    [InlineData("/auth/signin")]
    [InlineData("/AUTH/SIGNIN")]
    [InlineData("/auth/callback")]
    [InlineData("/operate/connections?sort=name")]
    [InlineData("/operate/connections?next=https://unlisted.example.com")]
    [InlineData("/operate/connections#details")]
    [InlineData("/operate//settings")]
    [InlineData("/operate/settings/")]
    [InlineData("/operate/with.dot")]
    [InlineData("/operate/with%20space")]
    public async Task DisallowedServerReturnPathsFallBackToHomeBeforeRequest(string returnTo)
    {
        using var fixture = new BrokerFixture();
        await fixture.Authentication.BeginAsync("entra", returnTo);
        Assert.Equal("/console/", fixture.Handler.ReturnTo);
        Assert.Equal(1, fixture.Handler.Authorizations);
    }

    [Fact]
    public async Task ReturnPathLengthBoundaryMatchesServerContract()
    {
        using var fixture = new BrokerFixture();
        var maxRoute = "/" + new string('a', 247);
        var tooLongRoute = "/" + new string('a', 248);

        await fixture.Authentication.BeginAsync("entra", maxRoute);
        await fixture.Authentication.BeginAsync("entra", tooLongRoute);

        Assert.Equal(new[] { "/console" + maxRoute, "/console/" }, fixture.Handler.ReturnDestinations);
    }

    [Theory]
    [InlineData("/console/", true)]
    [InlineData("/console/operate/settings", true)]
    [InlineData("/console/publish", true)]
    [InlineData("/console/operate/health", true)]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("/admin/", false)]
    [InlineData("https://evil.example/console/", false)]
    [InlineData("//evil.example/console/", false)]
    [InlineData("/console/../outside", false)]
    [InlineData("/console/%2e%2e/admin", false)]
    [InlineData("/console/auth", false)]
    [InlineData("/console/auth/signin", false)]
    [InlineData("/console//operate/settings", false)]
    [InlineData("/console/operate/settings/", false)]
    [InlineData("/console/operate/connections?next=https://evil.example", false)]
    [InlineData("/console/operate/connections#details", false)]
    [InlineData("/console/publish\n", false)]
    [InlineData("/console/publish\\other", false)]
    public void IsAllowedReturnPathMatchesServerContract(string? path, bool expected)
    {
        Assert.Equal(expected, BrowserOidcAuthentication.IsAllowedReturnPath(path));
    }

    [Fact]
    public async Task OlderBrokerRejectingSettingsReceivesHomeOnRetry()
    {
        using var fixture = new BrokerFixture();
        fixture.Handler.RejectSettingsReturn = true;

        var destination = await fixture.Authentication.BeginAsync("entra", "/operate/settings");

        Assert.Equal("https://identity.example/authorize?state=issued-state", destination.AbsoluteUri);
        Assert.Equal(new[] { "/console/operate/settings", "/console/" }, fixture.Handler.ReturnDestinations);
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
    [InlineData("https://other.example/console/")]
    [InlineData("/../outside")]
    [InlineData("/operate/../../outside")]
    [InlineData("/%2e%2e/outside")]
    [InlineData("/operate\\outside")]
    [InlineData("/operate/settings\n")]
    [InlineData("/operate/settings\r\n")]
    [InlineData("/\t/evil.example")]
    public async Task InvalidReturnPathCannotStartAnAuthorizationRequest(string returnTo)
    {
        using var fixture = new BrokerFixture();
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Authentication.BeginAsync("entra", returnTo));
        Assert.Equal(0, fixture.Handler.Authorizations);
    }

    [Fact]
    public async Task BuiltInPasswordModeStillListsAndStartsServerProviders()
    {
        using var fixture = new BrokerFixture(signIn: ConfiguredSignIn.BuiltIn);
        Assert.True(fixture.Authentication.IsAvailable);
        var provider = Assert.Single(await fixture.Authentication.GetProvidersAsync());
        Assert.Equal(("entra", "Organization"), (provider.Key, provider.DisplayName));
        await fixture.Authentication.BeginAsync("entra", "/");
        Assert.Equal(1, fixture.Handler.Authorizations);
    }

    [Fact]
    public async Task SeparateOriginCannotUseCookieBroker()
    {
        using var fixture = new BrokerFixture(crossOrigin: true);
        Assert.False(fixture.Authentication.IsAvailable);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Authentication.BeginAsync("entra", "/"));
        Assert.Equal(0, fixture.Handler.Authorizations);
    }

    private enum ConfiguredSignIn { External, BuiltIn }

    private sealed class BrokerFixture : IDisposable
    {
        public ConsoleBrowserSession Session { get; } = new();
        public BrokerHandler Handler { get; } = new();
        private readonly HttpClient _client;
        public BrowserOidcAuthentication Authentication { get; }
        public BrowserAuthenticationStateProvider Provider { get; }

        public BrokerFixture(bool crossOrigin = false, ConfiguredSignIn signIn = ConfiguredSignIn.External)
        {
            var origin = new Uri("https://honua.example/");
            _client = new HttpClient(new BrowserApiHandler(new BrowserApiOrigins(origin, origin), Session, BrowserApiAccess.Public)
            { InnerHandler = Handler })
            { BaseAddress = origin };
            var api = new ConsoleApiClient(_client) { SupportsPasswordSignIn = signIn == ConfiguredSignIn.BuiltIn, UsesCookieSession = !crossOrigin };
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
        public List<string?> ReturnDestinations { get; } = [];
        public int Authorizations => ReturnDestinations.Count;
        public string? ReturnTo => ReturnDestinations.LastOrDefault();
        public bool RejectSettingsReturn { get; set; }
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
                var returnTo = body!.RootElement.GetProperty("returnTo").GetString();
                Assert.Single(body.RootElement.EnumerateObject());
                ReturnDestinations.Add(returnTo);
                if (RejectSettingsReturn && returnTo == "/console/operate/settings")
                {
                    return new HttpResponseMessage(HttpStatusCode.BadRequest);
                }
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
