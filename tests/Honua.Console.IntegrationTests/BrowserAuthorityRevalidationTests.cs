using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Honua.Console.Web.Runtime;

namespace Honua.Console.IntegrationTests;

public sealed class BrowserAuthorityRevalidationTests
{
    private static readonly Uri Api = new("https://console.example/");

    [Fact]
    public async Task UnchangedServerSessionPreservesTheTabBinding()
    {
        var session = SignedIn();
        using var handler = new Handler(_ => Task.FromResult(Response()));
        using var authority = Validator(session, handler);
        var binding = (await authority.CaptureAsync())!;

        Assert.True(await authority.RevalidateAsync(binding));
        Assert.Equal(binding, await authority.CaptureAsync());
        Assert.True(handler.NoCache);
    }

    [Fact]
    public async Task ReauthenticationWhileTheServerProbeIsPendingRejectsTheResponse()
    {
        var response = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var session = SignedIn();
        using var handler = new Handler(_ => response.Task);
        using var authority = Validator(session, handler);
        var pending = authority.RevalidateAsync((await authority.CaptureAsync())!);
        session.Set(session.Principal);
        response.SetResult(Response());

        Assert.False(await pending);
    }

    [Theory]
    [InlineData("{", HttpStatusCode.OK)]
    [InlineData("{\"isAuthenticated\":true}", HttpStatusCode.OK)]
    [InlineData("{\"isAuthenticated\":42}", HttpStatusCode.OK)]
    [InlineData("{}", HttpStatusCode.Unauthorized)]
    [InlineData("{}", HttpStatusCode.ServiceUnavailable)]
    public async Task UnusableServerResponseRejectsTheBinding(string json, HttpStatusCode status)
    {
        var session = SignedIn();
        using var handler = new Handler(_ => Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        }));
        using var authority = Validator(session, handler);

        Assert.False(await authority.RevalidateAsync((await authority.CaptureAsync())!));
    }

    [Fact]
    public async Task NetworkFailureRejectsTheBinding()
    {
        var session = SignedIn();
        using var handler = new Handler(_ => throw new HttpRequestException("Offline"));
        using var authority = Validator(session, handler);

        Assert.False(await authority.RevalidateAsync((await authority.CaptureAsync())!));
    }

    [Fact]
    public async Task ExpiredServerSessionRejectsTheBinding()
    {
        var session = SignedIn();
        using var handler = new Handler(_ => Task.FromResult(Response(DateTimeOffset.UtcNow.AddMinutes(-1))));
        using var authority = Validator(session, handler);

        Assert.False(await authority.RevalidateAsync((await authority.CaptureAsync())!));
    }

    [Fact]
    public async Task MissingServerClientRejectsTheBinding()
    {
        using var authority = new BrowserAuthorityBindingValidator(SignedIn(), Api);
        Assert.False(await authority.RevalidateAsync((await authority.CaptureAsync())!));
    }

    private static BrowserAuthorityBindingValidator Validator(ConsoleBrowserSession session, HttpMessageHandler handler) =>
        new(session, Api, new ConsoleApiClient(new HttpClient(handler) { BaseAddress = Api }));

    private static ConsoleBrowserSession SignedIn()
    {
        var session = new ConsoleBrowserSession();
        session.Set(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "operator")], "test")));
        return session;
    }

    private static HttpResponseMessage Response(DateTimeOffset? expiresAt = null) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(new
        {
            isAuthenticated = true,
            expiresAt,
            claims = new[] { new { type = ClaimTypes.NameIdentifier, value = "operator" } }
        }), Encoding.UTF8, "application/json")
    };

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public bool NoCache { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("api/v1/admin/auth/session", request.RequestUri!.AbsolutePath.TrimStart('/'));
            Assert.Null(request.Headers.CacheControl);
            NoCache = request.Options.TryGetValue(
                new HttpRequestOptionsKey<IDictionary<string, object>>("WebAssemblyFetchOptions"), out var options)
                && options.TryGetValue("cache", out var cache) && Equals("no-store", cache);
            return send(request);
        }
    }
}
