using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Bunit;
using Honua.Console.Shell.Layout;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;
using Honua.Console.Web.Runtime;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Honua.Console.IntegrationTests;

public sealed class BrowserSignOutTests
{
    [Fact]
    public async Task HeaderShowsIdentityAndSignOutRevokesSessionBeforeNavigation()
    {
        using var fixture = new Fixture();
        var header = fixture.RenderHeader();
        header.WaitForAssertion(() => Assert.Contains("Test operator", header.Markup));
        await header.Find("[data-console-signout]").ClickAsync(new());
        header.WaitForAssertion(() => Assert.EndsWith("/auth/signin", fixture.Navigation.Uri));
        Assert.Equal(1, fixture.Handler.Logouts);
        Assert.False(fixture.Session.IsAuthenticated);
        Assert.Null(fixture.Session.AccessToken);
        Assert.Null(fixture.Session.CsrfToken);
        Assert.Null(await fixture.Sessions.GetSessionAsync("configured-api"));
        Assert.Empty(header.FindAll("[data-console-signout]"));
    }

    [Theory]
    [InlineData("""{"logoutUrl":"https://idp.example/logout?client_id=console"}""", "https://idp.example/logout?client_id=console")]
    [InlineData("""{"logoutUrl":null}""", null)]
    [InlineData("""{"logoutUrl":"http://idp.example/logout"}""", null)]
    [InlineData("", null)]
    public async Task SignOutLeavesForTheProviderLogoutOnlyWhenItIsHttps(string body, string? providerLogout)
    {
        using var fixture = new Fixture();
        fixture.Handler.LogoutBody = body;
        var header = fixture.RenderHeader();
        header.WaitForAssertion(() => Assert.Contains("Test operator", header.Markup));
        await header.Find("[data-console-signout]").ClickAsync(new());
        var navigation = fixture.Context.Services.GetRequiredService<Bunit.TestDoubles.BunitNavigationManager>();
        header.WaitForAssertion(() => Assert.NotEmpty(navigation.History));
        var navigated = navigation.History.First();
        Assert.EndsWith(providerLogout ?? "auth/signin", navigated.Uri, StringComparison.Ordinal);
        Assert.Equal(providerLogout is not null, navigated.Options.ForceLoad);
        Assert.False(fixture.Session.IsAuthenticated);
    }

    [Theory]
    [InlineData("Test operator", "TO")]
    [InlineData("kai@alder-county.example", "KA")]
    [InlineData("operator", "OP")]
    [InlineData("   ", "?")]
    [InlineData("émile zola", "ÉZ")]
    public void AvatarInitialsComeFromTheFirstTwoWordsOrTheFirstTwoLetters(string identity, string initials) =>
        Assert.Equal(initials, ConsoleAccountControl.Initials(identity));

    [Fact]
    public async Task FailedLogoutKeepsOperatorStateAndAllowsRetry()
    {
        using var fixture = new Fixture();
        fixture.Handler.LogoutStatus = HttpStatusCode.ServiceUnavailable;
        var header = fixture.RenderHeader();
        header.WaitForAssertion(() => Assert.Contains("Test operator", header.Markup));
        await header.Find("[data-console-signout]").ClickAsync(new());
        Assert.Contains("Sign-out could not finish", header.Find("[role=alert]").TextContent);
        Assert.True(fixture.Session.IsAuthenticated);
        Assert.NotNull(await fixture.Sessions.GetSessionAsync("configured-api"));
        Assert.DoesNotContain("/auth/signin", fixture.Navigation.Uri);
        Assert.False(header.Find("[data-console-signout]").HasAttribute("disabled"));
        fixture.Handler.LogoutStatus = HttpStatusCode.OK;
        await header.Find("[data-console-signout]").ClickAsync(new());
        header.WaitForAssertion(() => Assert.EndsWith("/auth/signin", fixture.Navigation.Uri));
        Assert.Equal(2, fixture.Handler.Logouts);
    }

    [Fact]
    public async Task CsrfFailureDoesNotReportLogoutOrDiscardTheSession()
    {
        using var fixture = new Fixture();
        await fixture.Authentication.GetAuthenticationStateAsync();
        fixture.Handler.CsrfStatus = HttpStatusCode.ServiceUnavailable;
        await Assert.ThrowsAsync<HttpRequestException>(() => fixture.Authentication.SignOutAsync());
        Assert.Equal(0, fixture.Handler.Logouts);
        Assert.True(fixture.Session.IsAuthenticated);
        Assert.NotNull(await fixture.Sessions.GetSessionAsync("configured-api"));
    }

    [Fact]
    public async Task PendingLogoutDisablesButtonAndKeepsSessionUntilServerConfirms()
    {
        using var fixture = new Fixture();
        fixture.Handler.PendingLogout = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var header = fixture.RenderHeader();
        header.WaitForAssertion(() => Assert.Contains("Test operator", header.Markup));
        var click = header.Find("[data-console-signout]").ClickAsync(new());
        header.WaitForAssertion(() => Assert.True(header.Find("[data-console-signout]").HasAttribute("disabled")));
        Assert.True(fixture.Session.IsAuthenticated);
        Assert.DoesNotContain("/auth/signin", fixture.Navigation.Uri);
        Assert.Equal(1, fixture.Handler.Logouts);
        fixture.Handler.PendingLogout.SetResult(new HttpResponseMessage(HttpStatusCode.OK));
        await click;
        Assert.False(fixture.Session.IsAuthenticated);
        Assert.EndsWith("/auth/signin", fixture.Navigation.Uri);
    }

    [Fact]
    public async Task LogoutRefreshesCsrfAndUsesTheCookieTransport()
    {
        using var fixture = new Fixture();
        await fixture.Authentication.GetAuthenticationStateAsync();
        fixture.Session.RememberManagementBearer("manager-token", DateTimeOffset.UtcNow.AddHours(1));
        fixture.Handler.CsrfToken = "fresh-logout-csrf";
        await fixture.Authentication.SignOutAsync();
        Assert.Equal("fresh-logout-csrf", fixture.Handler.LogoutCsrf);
        Assert.True(fixture.Handler.LogoutUsedCookies);
        Assert.Null(fixture.Handler.LogoutAuthorization);
        Assert.Null(fixture.Session.ManagementAccessToken);
        Assert.False((await fixture.Authentication.GetAuthenticationStateAsync()).User.Identity!.IsAuthenticated);
    }

    [Fact]
    public async Task BearerSignOutClearsBothAuthoritiesWithoutCallingCookieLogout()
    {
        using var fixture = new Fixture(usesCookies: false);
        await fixture.Authentication.GetAuthenticationStateAsync();
        fixture.Session.RememberManagementBearer("manager-token", DateTimeOffset.UtcNow.AddHours(1));
        await fixture.Authentication.SignOutAsync();
        Assert.Equal(0, fixture.Handler.Logouts);
        Assert.False(fixture.Session.IsAuthenticated);
        Assert.Null(fixture.Session.AccessToken);
        Assert.Null(fixture.Session.ManagementAccessToken);
        Assert.Null(await fixture.Sessions.GetSessionAsync("configured-api"));
    }

    [Fact]
    public async Task AnonymousHeaderDoesNotShowAccountControl()
    {
        using var fixture = new Fixture();
        fixture.Handler.Authenticated = false;
        var header = fixture.RenderHeader();
        await fixture.Authentication.GetAuthenticationStateAsync();
        Assert.Empty(header.FindAll("[data-console-signout]"));
        Assert.DoesNotContain("Test operator", header.Markup);
    }

    [Theory]
    [InlineData("/", "/auth/signin")]
    [InlineData("/operate/services?sort=name", "/auth/signin?returnTo=%2Foperate%2Fservices%3Fsort%3Dname")]
    public async Task AnonymousHeaderOffersSignInThatReturnsToTheCurrentRoute(string location, string expected)
    {
        using var fixture = new Fixture();
        fixture.Handler.Authenticated = false;
        fixture.Navigation.NavigateTo(location);
        var header = fixture.RenderHeader();
        await fixture.Authentication.GetAuthenticationStateAsync();
        header.WaitForAssertion(() => Assert.Equal(expected, header.Find("[data-console-signin]").GetAttribute("href")));
        fixture.Navigation.NavigateTo("/Auth/SignIn/?returnTo=%2F");
        header.WaitForAssertion(() => Assert.Empty(header.FindAll("[data-console-signin]")));
    }

    [Fact]
    public void AccountControlIsOutsideTheCollapsiblePrimaryNavigation()
    {
        using var fixture = new Fixture();
        var header = fixture.RenderHeader();
        header.WaitForAssertion(() => Assert.Single(header.FindAll("[data-console-signout]")));
        Assert.Empty(header.Find("#console-primary").QuerySelectorAll("[data-console-signout]"));
        Assert.Equal("button", header.Find("[data-console-signout]").GetAttribute("type"));
        Assert.Equal("Sign out", header.Find("[data-console-signout]").TextContent);
    }

    [Theory]
    [InlineData("", "operator")]
    [InlineData("Long operator name from identity provider", "Long operator name from identity provider")]
    public void HeaderUsesAvailableIdentityWithoutHidingSignOut(string displayName, string expected)
    {
        using var fixture = new Fixture();
        fixture.Handler.DisplayName = displayName;
        var header = fixture.RenderHeader();
        header.WaitForAssertion(() => Assert.Equal(expected, header.Find(".console-account-identity").TextContent));
        Assert.Single(header.FindAll("[data-console-signout]"));
    }

    private sealed class Fixture : IDisposable
    {
        public BunitContext Context { get; } = new();
        public Handler Handler { get; } = new();
        public ConsoleBrowserSession Session { get; } = new();
        public InMemoryConsoleAccountSessionStore Sessions { get; } = new();
        private readonly HttpClient http;
        public BrowserAuthenticationStateProvider Authentication { get; }
        public NavigationManager Navigation => Context.Services.GetRequiredService<NavigationManager>();

        public Fixture(bool usesCookies = true)
        {
            var origin = new Uri("https://console.example/");
            http = new HttpClient(new BrowserApiHandler(new BrowserApiOrigins(origin, origin), Session, BrowserApiAccess.Public)
            {
                InnerHandler = Handler
            })
            { BaseAddress = origin };
            Session.Set(new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, "operator"),
                new Claim(ClaimTypes.Name, "Test operator")
            ], "test")),
                new ConsoleSessionCredentials("csrf-token", "serving-token", DateTimeOffset.UtcNow.AddHours(1)));
            Authentication = new BrowserAuthenticationStateProvider(new ConsoleApiClient(http) { UsesCookieSession = usesCookies }, Session, Sessions);
            Context.AddAuthorization();
            Context.Services.RemoveAll<AuthenticationStateProvider>();
            Context.Services.RemoveAll<IAuthorizationService>();
            Context.Services.RemoveAll<IAuthorizationPolicyProvider>();
            Context.Services.AddAuthorizationCore();
            Context.Services.AddSingleton<AuthenticationStateProvider>(Authentication);
            Context.Services.AddSingleton<IConsoleBrowserAuthentication>(Authentication);
        }

        public IRenderedComponent<CascadingAuthenticationState> RenderHeader() =>
            Context.Render<CascadingAuthenticationState>(parameters => parameters.AddChildContent<ConsoleTopNavigation>());

        public void Dispose()
        {
            Context.Dispose();
            Authentication.Dispose();
            http.Dispose();
        }
    }

    private sealed class Handler : HttpMessageHandler
    {
        public int Logouts { get; private set; }
        public HttpStatusCode LogoutStatus { get; set; } = HttpStatusCode.OK;
        public string LogoutBody { get; set; } = "";
        public TaskCompletionSource<HttpResponseMessage>? PendingLogout { get; set; }
        public HttpStatusCode CsrfStatus { get; set; } = HttpStatusCode.OK;
        public string CsrfToken { get; set; } = "csrf-token";
        public bool Authenticated { get; set; } = true;
        public string DisplayName { get; set; } = "Test operator";
        public string? LogoutCsrf { get; private set; }
        public string? LogoutAuthorization { get; private set; }
        public bool LogoutUsedCookies { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/logout", StringComparison.Ordinal))
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                Logouts++;
                LogoutCsrf = request.Headers.GetValues("X-Honua-CSRF").Single();
                LogoutAuthorization = request.Headers.Authorization?.ToString();
                LogoutUsedCookies = request.Options.TryGetValue(new HttpRequestOptionsKey<IDictionary<string, object>>("WebAssemblyFetchOptions"), out var options)
                    && options.TryGetValue("credentials", out var credentials) && Equals(credentials, "same-origin");
                return PendingLogout?.Task ?? Task.FromResult(new HttpResponseMessage(LogoutStatus) { Content = new StringContent(LogoutBody) });
            }
            var csrf = request.RequestUri.AbsolutePath.EndsWith("/csrf", StringComparison.Ordinal);
            return Task.FromResult(new HttpResponseMessage(csrf ? CsrfStatus : HttpStatusCode.OK)
            {
                Content = csrf
                    ? JsonContent.Create(new { requestToken = CsrfToken })
                    : JsonContent.Create(new
                    {
                        isAuthenticated = Authenticated,
                        claims = string.IsNullOrEmpty(DisplayName)
                        ? new[] { new { type = ClaimTypes.NameIdentifier, value = "operator" } }
                        : [new { type = ClaimTypes.NameIdentifier, value = "operator" },
                           new { type = ClaimTypes.Name, value = DisplayName }]
                    })
            });
        }
    }
}
