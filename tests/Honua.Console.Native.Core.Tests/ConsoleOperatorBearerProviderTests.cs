using System.Net;
using System.Text;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Security;
using Honua.Console.Shell.Services;

namespace Honua.Console.Native.Core.Tests;

public sealed class ConsoleOperatorBearerProviderTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-07-10T08:00:00Z");

    [Fact]
    public async Task Resolve_SentinelSession_ExchangesAndStoresShortLivedBearerForProfile()
    {
        var sessions = new InMemoryConsoleAccountSessionStore();
        await sessions.SaveSessionAsync(Session("env-a", ConsoleAuthConstants.SessionSentinelPrefix + "env-a"));
        var expiresAt = Now.AddMinutes(5);
        var exchange = new StubExchange(_ => ConsoleOperatorBearerExchangeResult.Issued("bearer-a", expiresAt));
        var provider = new ConsoleOperatorBearerProvider(sessions, exchange, new FixedTimeProvider(Now));

        var result = await provider.ResolveAsync(Profile("env-a"));

        Assert.True(result.IsAvailable);
        Assert.True(result.HasInteractiveSession);
        Assert.Equal("bearer-a", result.AccessToken);
        Assert.Equal(expiresAt, result.ExpiresAt);
        Assert.Equal(["env-a"], exchange.ProfileIds);
        var stored = await sessions.GetSessionAsync("env-a");
        Assert.Equal("bearer-a", stored!.AccessToken);
        Assert.Equal(expiresAt, stored.AccessTokenExpiresAt);
    }

    [Fact]
    public async Task Resolve_ProfileSwitch_KeepsBearerAndRefreshPartitionedByEnvironment()
    {
        var sessions = new InMemoryConsoleAccountSessionStore();
        await sessions.SaveSessionAsync(Session("env-a", ConsoleAuthConstants.SessionSentinelPrefix + "env-a"));
        await sessions.SaveSessionAsync(Session("env-b", ConsoleAuthConstants.SessionSentinelPrefix + "env-b"));
        var exchange = new StubExchange(profile =>
            ConsoleOperatorBearerExchangeResult.Issued($"bearer-{profile.Id}", Now.AddMinutes(5)));
        var provider = new ConsoleOperatorBearerProvider(sessions, exchange, new FixedTimeProvider(Now));

        var first = await provider.ResolveAsync(Profile("env-a"));
        var second = await provider.ResolveAsync(Profile("env-b"));

        Assert.Equal("bearer-env-a", first.AccessToken);
        Assert.Equal("bearer-env-b", second.AccessToken);
        Assert.Equal("bearer-env-a", (await sessions.GetSessionAsync("env-a"))!.AccessToken);
        Assert.Equal("bearer-env-b", (await sessions.GetSessionAsync("env-b"))!.AccessToken);
    }

    [Fact]
    public async Task Resolve_ExpiredBearer_RefreshesBeforeMutation()
    {
        var sessions = new InMemoryConsoleAccountSessionStore();
        await sessions.SaveSessionAsync(Session("env-a", "expired-bearer") with
        {
            AccessTokenExpiresAt = Now.AddSeconds(-1)
        });
        var exchange = new StubExchange(_ =>
            ConsoleOperatorBearerExchangeResult.Issued("refreshed-bearer", Now.AddMinutes(5)));
        var provider = new ConsoleOperatorBearerProvider(sessions, exchange, new FixedTimeProvider(Now));

        var result = await provider.ResolveAsync(Profile("env-a"));

        Assert.Equal("refreshed-bearer", result.AccessToken);
        Assert.Single(exchange.ProfileIds);
    }

    [Fact]
    public async Task Resolve_ExchangeDenied_ReturnsReauthenticationStateWithoutReplacingSession()
    {
        var sessions = new InMemoryConsoleAccountSessionStore();
        var sentinel = Session("env-a", ConsoleAuthConstants.SessionSentinelPrefix + "env-a");
        await sessions.SaveSessionAsync(sentinel);
        var exchange = new StubExchange(_ =>
            ConsoleOperatorBearerExchangeResult.Denied("The server admin session expired."));
        var provider = new ConsoleOperatorBearerProvider(sessions, exchange, new FixedTimeProvider(Now));

        var result = await provider.ResolveAsync(Profile("env-a"));

        Assert.False(result.IsAvailable);
        Assert.True(result.HasInteractiveSession);
        Assert.Contains("sign in", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(sentinel, await sessions.GetSessionAsync("env-a"));
    }

    [Fact]
    public async Task HttpExchange_PostsShippedServerContractWithoutApiKeyOrActorHeader()
    {
        const string body = """
        {
          "accessToken": "server-issued-bearer",
          "tokenType": "Bearer",
          "expiresAt": "2026-07-10T08:05:00Z",
          "expiresIn": 300
        }
        """;
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        });
        var exchange = new HttpConsoleOperatorBearerExchange(new HttpClient(handler));

        var result = await exchange.ExchangeAsync(Profile("env-a"));

        Assert.Equal(ConsoleOperatorBearerExchangeStatus.Issued, result.Status);
        Assert.Equal("server-issued-bearer", result.AccessToken);
        Assert.Equal(DateTimeOffset.Parse("2026-07-10T08:05:00Z"), result.ExpiresAt);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/api/v1/admin/auth/bearer", request.RequestUri!.AbsolutePath);
        Assert.False(request.Headers.Contains("X-API-Key"));
        Assert.DoesNotContain(request.Headers, header => header.Key.Contains("Actor", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task HttpExchange_Unauthorized_MapsToReauthenticationRequired()
    {
        var exchange = new HttpConsoleOperatorBearerExchange(new HttpClient(
            new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized))));

        var result = await exchange.ExchangeAsync(Profile("env-a"));

        Assert.Equal(ConsoleOperatorBearerExchangeStatus.Denied, result.Status);
        Assert.Contains("sign in", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HttpExchange_RejectsClientConfiguredWithApiKeyOrActorHeadersWithoutSending()
    {
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("Request must not be sent."));
        var http = new HttpClient(handler);
        http.DefaultRequestHeaders.TryAddWithoutValidation("X-API-Key", "shared-admin-key");
        http.DefaultRequestHeaders.TryAddWithoutValidation("X-Honua-Actor", "operator.alice");
        var exchange = new HttpConsoleOperatorBearerExchange(http);

        var result = await exchange.ExchangeAsync(Profile("env-a"));

        Assert.Equal(ConsoleOperatorBearerExchangeStatus.Unavailable, result.Status);
        Assert.Contains("misconfigured", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(handler.Requests);
    }

    private static ConsoleAccountSession Session(string profileId, string token) => new()
    {
        ProfileId = profileId,
        AccountId = "operator.alice",
        DisplayName = "Alice Operator",
        AccessToken = token
    };

    private static ConsoleEnvironmentProfile Profile(string id) => new()
    {
        Id = id,
        ServerBaseUri = new Uri("https://server.example"),
        Account = new ConsoleAccountBinding
        {
            AuthMode = ConsoleAccountAuthMode.AccountRbac,
            AccountId = "operator.alice"
        }
    };

    private sealed class StubExchange(
        Func<ConsoleEnvironmentProfile, ConsoleOperatorBearerExchangeResult> response)
        : IConsoleOperatorBearerExchange
    {
        public List<string> ProfileIds { get; } = [];

        public Task<ConsoleOperatorBearerExchangeResult> ExchangeAsync(
            ConsoleEnvironmentProfile profile,
            CancellationToken cancellationToken = default)
        {
            ProfileIds.Add(profile.Id);
            return Task.FromResult(response(profile));
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(Clone(request));
            return Task.FromResult(response(request));
        }

        private static HttpRequestMessage Clone(HttpRequestMessage request)
        {
            var clone = new HttpRequestMessage(request.Method, request.RequestUri);
            foreach (var header in request.Headers)
            {
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            return clone;
        }
    }
}
