using System.Net;
using System.Security.Claims;
using Honua.Console.Contracts;
using Honua.Console.Web.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class BrowserAuthorityBindingTests
{
    private static readonly Uri Api = new("https://console.example/");

    [Fact]
    public async Task ProductionRegistrationCapturesTheSessionUsedByTheTransport()
    {
        var session = new ConsoleBrowserSession();
        using var services = new ServiceCollection().AddSingleton(session).AddBrowserAuthorityBinding(Api).BuildServiceProvider();
        var authority = services.GetRequiredService<IConsoleAuthorityBindingValidator>();
        Assert.Null(await authority.CaptureAsync());
        SignIn(session);
        var binding = Assert.IsType<string>(await authority.CaptureAsync());
        Assert.True(await authority.IsCurrentAsync(binding));
        using var client = Client(session, new ResponseHandler());
        using var request = PinnedRequest(binding);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(request.Options.TryGetValue(ConsoleAuthorityRequestOptions.Resolved, out var resolved));
        Assert.Equal(binding, resolved);
    }

    [Fact]
    public async Task BindingCannotBeReusedAfterReauthenticationOrForAnotherApi()
    {
        var session = new ConsoleBrowserSession();
        SignIn(session);
        var authority = new BrowserAuthorityBindingValidator(session, Api);
        var binding = Assert.IsType<string>(await authority.CaptureAsync());
        var otherApi = new BrowserAuthorityBindingValidator(session, new Uri("https://other.example/"));
        Assert.False(await otherApi.IsCurrentAsync(binding));
        SignIn(session);
        Assert.False(await authority.IsCurrentAsync(binding));
        var handler = new ResponseHandler();
        using var client = Client(session, handler);
        using var request = PinnedRequest(binding);
        await Assert.ThrowsAsync<HttpRequestException>(() => client.SendAsync(request));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task ResponseFromAnEarlierSessionCannotExposeItsSecret()
    {
        var session = new ConsoleBrowserSession();
        SignIn(session);
        var binding = Assert.IsType<string>(await new BrowserAuthorityBindingValidator(session, Api).CaptureAsync());
        var completion = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new ResponseHandler { Pending = completion.Task };
        using var client = Client(session, handler);
        using var request = PinnedRequest(binding);
        var send = client.SendAsync(request);
        Assert.Equal(1, handler.Calls);
        session.Clear();
        completion.SetResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("stale-secret") });
        using var response = await send;
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.True(ConsoleWriteOutcome.IsUnverified(request));
        Assert.DoesNotContain("stale-secret", await response.Content.ReadAsStringAsync());
    }

    private static void SignIn(ConsoleBrowserSession session) => session.Set(
        new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "operator")], "test")),
        new ConsoleSessionCredentials(CsrfToken: "issued-csrf"));

    private static HttpClient Client(ConsoleBrowserSession session, HttpMessageHandler handler) => new(
        new BrowserApiHandler(new BrowserApiOrigins(Api, Api), session, BrowserApiAccess.Authenticated) { InnerHandler = handler });

    private static HttpRequestMessage PinnedRequest(string binding)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri(Api, "api/v1/admin/api-keys/"));
        request.Options.Set(ConsoleAuthorityRequestOptions.Expected, binding);
        return request;
    }

    private sealed class ResponseHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public Task<HttpResponseMessage>? Pending { get; init; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Pending ?? Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
