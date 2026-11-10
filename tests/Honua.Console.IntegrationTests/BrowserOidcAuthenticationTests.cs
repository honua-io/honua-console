using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Honua.Console.Shell.Services;
using Honua.Console.Web.Runtime;
using Microsoft.JSInterop;

namespace Honua.Console.IntegrationTests;

public sealed class BrowserOidcAuthenticationTests
{
    [Fact]
    public async Task BrokerRoundTripRefreshesCsrfAndConsumesCorrelationWithoutStoringCredentials()
    {
        using var fixture = new BrokerFixture();
        var destination = await fixture.Authentication.BeginAsync("entra", "/operate/connections");
        Assert.Equal("https://identity.example/authorize?state=issued-state", destination.AbsoluteUri);
        var pending = Assert.Single(fixture.Storage.Values).Value;
        Assert.DoesNotContain("fixture-code", pending);
        Assert.DoesNotContain("accessToken", pending);
        var returnTo = await fixture.Authentication.CompleteAsync("fixture-code", "issued-state");
        Assert.Equal("/operate/connections", returnTo);
        Assert.Empty(fixture.Storage.Values);
        Assert.True(fixture.Session.IsAuthenticated);
        Assert.Equal("identified-csrf", fixture.Session.CsrfToken);
        Assert.Null(fixture.Session.AccessToken);
        Assert.Equal(1, fixture.Handler.Exchanges);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Authentication.CompleteAsync("fixture-code", "issued-state"));
        Assert.Equal(1, fixture.Handler.Exchanges);
    }

    [Theory]
    [InlineData("wrong-state", 0)]
    [InlineData("issued-state", -11)]
    [InlineData("issued-state", 1)]
    public async Task InvalidCorrelationIsConsumedBeforeAnyTokenExchange(string state, int ageMinutes)
    {
        using var fixture = new BrokerFixture();
        await fixture.Authentication.BeginAsync("entra", "/operate/connections");
        if (ageMinutes != 0)
        {
            var key = Assert.Single(fixture.Storage.Values).Key;
            fixture.Storage.Values[key] = JsonSerializer.Serialize(new
            {
                ProviderKey = "entra",
                State = "issued-state",
                ReturnTo = "/operate/connections",
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(ageMinutes)
            });
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Authentication.CompleteAsync("fixture-code", state));
        Assert.Empty(fixture.Storage.Values);
        Assert.Equal(0, fixture.Handler.Exchanges);
        Assert.False(fixture.Session.IsAuthenticated);
    }

    [Fact]
    public async Task UnknownProviderCannotStartAnAuthorizationRequest()
    {
        using var fixture = new BrokerFixture();
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Authentication.BeginAsync("unknown", "/"));
        Assert.Empty(fixture.Storage.Values);
        Assert.Equal(0, fixture.Handler.Authorizations);
    }

    private sealed class BrokerFixture : IDisposable
    {
        public ConsoleBrowserSession Session { get; } = new();
        public MemoryStorage Storage { get; } = new();
        public BrokerHandler Handler { get; } = new();
        private readonly HttpClient _client;
        public BrowserOidcAuthentication Authentication { get; }

        public BrokerFixture()
        {
            var origin = new Uri("https://honua.example/");
            _client = new HttpClient(new BrowserApiHandler(new BrowserApiOrigins(origin, origin), Session, BrowserApiAccess.Public)
            { InnerHandler = Handler })
            { BaseAddress = origin };
            var api = new ConsoleApiClient(_client) { SupportsPasswordSignIn = false };
            var provider = new BrowserAuthenticationStateProvider(api, Session, new InMemoryConsoleAccountSessionStore());
            Authentication = new BrowserOidcAuthentication(api, provider, Storage);
        }

        public void Dispose() => _client.Dispose();
    }

    private sealed class MemoryStorage : IJSRuntime
    {
        public Dictionary<string, string> Values { get; } = [];
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, CancellationToken.None, args);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            var key = (string)args![0]!;
            object? result = null;
            switch (identifier)
            {
                case "sessionStorage.setItem": Values[key] = (string)args[1]!; break;
                case "sessionStorage.getItem": Values.TryGetValue(key, out var value); result = value; break;
                case "sessionStorage.removeItem": Values.Remove(key); break;
                default: throw new InvalidOperationException(identifier);
            }
            return ValueTask.FromResult(result is null ? default! : (TValue)result);
        }
    }

    private sealed class BrokerHandler : HttpMessageHandler
    {
        public int Authorizations { get; private set; }
        public int Exchanges { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Post)
            {
                Assert.Equal("anonymous-csrf", Assert.Single(request.Headers.GetValues("X-Honua-CSRF")));
                Assert.Null(request.Headers.Authorization);
                if (path.EndsWith("/authorize-url", StringComparison.Ordinal))
                {
                    Authorizations++;
                    return Json(new { authorizeUrl = "https://identity.example/authorize?state=issued-state" });
                }
                Assert.EndsWith("/providers/entra/token", path);
                using var body = await request.Content!.ReadFromJsonAsync<JsonDocument>(cancellationToken);
                Assert.Equal("fixture-code", body!.RootElement.GetProperty("code").GetString());
                Assert.Equal("issued-state", body.RootElement.GetProperty("state").GetString());
                Assert.False(body.RootElement.TryGetProperty("clientSecret", out _));
                Exchanges++;
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
            if (path.EndsWith("/csrf", StringComparison.Ordinal))
            {
                return Json(new { requestToken = Exchanges > 0 ? "identified-csrf" : "anonymous-csrf" });
            }
            if (path.EndsWith("/config", StringComparison.Ordinal))
            {
                return Json(new { oidcEnabled = true, providers = new[] { new { key = "entra", displayName = "Organization" } } });
            }
            Assert.EndsWith("/session", path);
            return Json(new { isAuthenticated = true, claims = new[] { new { type = ClaimTypes.NameIdentifier, value = "operator" } }, expiresAt = DateTimeOffset.UtcNow.AddMinutes(5) });
        }
        private static HttpResponseMessage Json(object body) => new(HttpStatusCode.OK) { Content = JsonContent.Create(body) };
    }
}
