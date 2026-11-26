using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Bunit;
using Honua.Console.Contracts;
using Honua.Console.Shell.Layout;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;
using Honua.Console.Web.Runtime;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using static Honua.Console.IntegrationTests.BrowserApiTransportTests;
using static Honua.Console.IntegrationTests.ConsolePublicationRenderFixture;

namespace Honua.Console.IntegrationTests;

public sealed class BrowserSessionInvalidationTests
{
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, false)]
    [InlineData(HttpStatusCode.Forbidden, true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, true)]
    public async Task ProtectedResponseUpdatesOnlyRejectedSessions(HttpStatusCode status, bool remainsAuthenticated)
    {
        var session = SignedInSession();
        using var client = ProtectedClient(session, new ResponseHandler(status));
        using var request = new HttpRequestMessage(HttpMethod.Get, "services");
        request.Options.Set(ConsoleAuthorityRequestOptions.Capture, true);
        using var response = await client.SendAsync(request);
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(remainsAuthenticated, session.IsAuthenticated);
        if (!remainsAuthenticated) Assert.Null(session.AccessToken);
    }

    [Fact]
    public Task DefaultOriginManagerUnauthorizedClearsServingSession() =>
        AssertServingOriginManagerUnauthorizedClearsSession(managementOrigin: null);

    [Fact]
    public Task ExplicitSameOriginManagerUnauthorizedClearsServingSession() =>
        AssertServingOriginManagerUnauthorizedClearsSession(managementOrigin: Api);

    private static async Task AssertServingOriginManagerUnauthorizedClearsSession(Uri? managementOrigin)
    {
        var session = SignedInSession();
        var origins = new BrowserApiOrigins(Api, App, managementOrigin);
        using var client = new HttpClient(new BrowserApiHandler(origins, session, BrowserApiAccess.Manager)
        {
            InnerHandler = new ResponseHandler(HttpStatusCode.Unauthorized)
        })
        { BaseAddress = Api };
        using var response = await client.GetAsync("services");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(session.IsAuthenticated);
        Assert.Null(session.AccessToken);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnrelatedUnauthorizedResponsesPreserveServingSession(bool management)
    {
        var session = SignedInSession();
        var origins = new BrowserApiOrigins(Api, App, new Uri("https://manager.example/"));
        using var client = new HttpClient(new BrowserApiHandler(origins, session,
            management ? BrowserApiAccess.Manager : BrowserApiAccess.Public)
        {
            InnerHandler = new ResponseHandler(HttpStatusCode.Unauthorized)
        })
        { BaseAddress = Api };
        using var response = await client.GetAsync("services");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True(session.IsAuthenticated);
    }

    [Fact]
    public async Task DelayedUnauthorizedResponseCannotClearReplacementSession()
    {
        var session = SignedInSession();
        var handler = new PendingHandler();
        using var client = ProtectedClient(session, handler);
        var pending = client.GetAsync("services");
        session.Set(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "replacement")], "test")));
        handler.Completion.SetResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        using var response = await pending;
        Assert.True(session.IsAuthenticated);
        Assert.Equal("replacement", session.Principal.FindFirstValue(ClaimTypes.NameIdentifier));
    }

    [Fact]
    public async Task DelayedBootstrapCannotRestoreClearedSession()
    {
        var session = SignedInSession();
        var handler = new PendingHandler();
        using var http = new HttpClient(handler) { BaseAddress = Api };
        using var authentication = Provider(http, session);
        var pending = authentication.GetAuthenticationStateAsync();
        session.Clear();
        handler.Completion.SetResult(SessionResponse());
        Assert.False((await pending).User.Identity!.IsAuthenticated);
        Assert.False((await authentication.GetAuthenticationStateAsync()).User.Identity!.IsAuthenticated);
        Assert.False(session.IsAuthenticated);
    }

    [Fact]
    public async Task RejectedSessionRemovesNavigationAndCannotRestoreThePreviousBreadcrumb()
    {
        var source = new PublicationNavigationSource
        {
            ServiceSummaries = [new() { ServiceName = "parcels", FolderId = "public", FolderName = "Public", LayerCount = 1 }]
        };
        var session = SignedInSession();
        using var http = new HttpClient(new BootstrapHandler()) { BaseAddress = Api };
        using var authentication = Provider(http, session);
        Assert.True((await authentication.GetAuthenticationStateAsync()).User.Identity!.IsAuthenticated);
        using var context = new BunitContext();
        context.AddAuthorization();
        ConfigurePublicationLayout(context, source);
        context.Services.RemoveAll<AuthenticationStateProvider>();
        context.Services.RemoveAll<IAuthorizationService>();
        context.Services.RemoveAll<IAuthorizationPolicyProvider>();
        context.Services.AddAuthorizationCore();
        context.Services.AddSingleton<AuthenticationStateProvider>(authentication);
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("/operate/services/parcels/settings");
        var layout = RenderLayerLayout(context);
        layout.WaitForAssertion(() => Assert.Contains("Public", layout.Find("nav[aria-label='Breadcrumb']").TextContent));
        var publication = layout.FindComponent<CascadingValue<ConsolePublicationTreeContext>>().Instance.Value;
        var previousPath = publication.Tree.PathTo("/operate/services/parcels/settings");
        var publish = layout.FindComponent<ConsolePublicationNavigation>().Instance.SelectionChanged;
        using var client = ProtectedClient(session, new ResponseHandler(HttpStatusCode.Unauthorized));
        await layout.InvokeAsync(async () => { using var response = await client.GetAsync("services"); });
        layout.WaitForAssertion(() => Assert.Empty(layout.FindAll("[role='tree']")));
        layout.WaitForAssertion(() => Assert.Empty(layout.FindAll("nav[aria-label='Breadcrumb']")));
        await layout.InvokeAsync(() => publish.InvokeAsync(previousPath));
        Assert.Empty(layout.FindAll("nav[aria-label='Breadcrumb']"));
        Assert.False((await authentication.GetAuthenticationStateAsync()).User.Identity!.IsAuthenticated);
    }

    private static ConsoleBrowserSession SignedInSession()
    {
        var session = new ConsoleBrowserSession();
        session.Set(Principal(), new ConsoleSessionCredentials(AccessToken: "test-token", ExpiresAt: DateTimeOffset.UtcNow.AddMinutes(5)));
        return session;
    }

    private static HttpClient ProtectedClient(ConsoleBrowserSession session, HttpMessageHandler handler) => new(
        new BrowserApiHandler(new BrowserApiOrigins(Api, App), session, BrowserApiAccess.Authenticated)
        { InnerHandler = handler })
    { BaseAddress = Api };

    private static BrowserAuthenticationStateProvider Provider(HttpClient http, ConsoleBrowserSession session) =>
        new(new ConsoleApiClient(http) { UsesCookieSession = false }, session, new InMemoryConsoleAccountSessionStore());

    private static HttpResponseMessage SessionResponse() => new(HttpStatusCode.OK)
    {
        Content = JsonContent.Create(new
        {
            isAuthenticated = true,
            claims = new[] { new { type = ClaimTypes.NameIdentifier, value = "operator" } }
        })
    };

    private sealed class ResponseHandler(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status));
    }

    private sealed class PendingHandler : HttpMessageHandler
    {
        public TaskCompletionSource<HttpResponseMessage> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Completion.Task;
    }

    private sealed class BootstrapHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(SessionResponse());
    }
}
