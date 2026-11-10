using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class BrowserSignInPageTests
{
    [Fact]
    public async Task ExpiredBrowserSessionLinksToMountedSignInAndPreservesDestination()
    {
        await using var context = CreateContext();
        context.Services.AddSingleton<IConsoleBrowserAuthentication>(new PendingAuthentication());
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/console/operate/connections");
        var notice = context.Render<ConsoleConnectionNotice>(parameters => parameters.Add(value => value.States,
            new[] { new OperateCapabilityState("Resources", "Unauthorized", "contract", "Expired") { StatusCode = 401 } }));
        Assert.Equal("/console/auth/signin?returnTo=%2Foperate%2Fconnections", notice.Find("a").GetAttribute("href"));
        context.Render<RedirectToLogin>();
        Assert.Equal("http://localhost/console/auth/signin?returnTo=%2Foperate%2Fconnections", navigation.Uri);
    }

    [Fact]
    public async Task PasswordSubmissionDisablesDuplicatesAndKeepsTheApplicationBase()
    {
        await using var context = CreateContext();
        var authentication = new PendingAuthentication();
        context.Services.AddSingleton<IConsoleBrowserAuthentication>(authentication);
        var page = context.Render<AuthSignInPage>();
        page.Find("input[type=password]").Change("operator-password");
        var submission = page.Find("form").SubmitAsync();
        page.WaitForAssertion(() => Assert.True(page.Find("button").HasAttribute("disabled")));
        Assert.Equal("operator-password", authentication.Password);
        Assert.Equal("/console/operate/connections", page.Find("a").GetAttribute("href"));
        authentication.Completion.SetResult();
        await submission;
        Assert.Equal("http://localhost/console/operate/connections", context.Services.GetRequiredService<NavigationManager>().Uri);
    }

    [Fact]
    public async Task DeniedPasswordClearsTheFieldAndAllowsRetryWithoutExposingServerDetails()
    {
        await using var context = CreateContext();
        var authentication = new PendingAuthentication();
        context.Services.AddSingleton<IConsoleBrowserAuthentication>(authentication);
        var page = context.Render<AuthSignInPage>();
        page.Find("input[type=password]").Change("operator-password");
        var submission = page.Find("form").SubmitAsync();
        authentication.Completion.SetException(new HttpRequestException("private server details", null, System.Net.HttpStatusCode.Unauthorized));
        await submission;
        Assert.Contains("The password was not accepted", page.Find("[role=alert]").TextContent);
        Assert.DoesNotContain("private server details", page.Markup);
        Assert.Equal(string.Empty, page.Find("input").GetAttribute("value") ?? string.Empty);
        Assert.False(page.Find("button").HasAttribute("disabled"));
        Assert.EndsWith("/auth/signin?returnTo=%2Foperate%2Fconnections", context.Services.GetRequiredService<NavigationManager>().Uri);
    }

    [Fact]
    public async Task UnsupportedIdentityProviderDoesNotSendPasswordOrUseTheLegacyHostBridge()
    {
        await using var context = CreateContext();
        context.Services.AddSingleton<IConsoleBrowserAuthentication>(new PendingAuthentication { SupportsPasswordSignIn = false });
        var page = context.Render<AuthSignInPage>();
        Assert.Empty(page.FindAll("input, button"));
        Assert.Contains("not available", page.Find("[role=status]").TextContent);
    }

    [Fact]
    public async Task ProviderSelectionUsesTheServerLabelAndPreservesReturnRoute()
    {
        await using var context = CreateContext();
        var oidc = new StubOidc();
        context.Services.AddSingleton<IConsoleBrowserAuthentication>(new PendingAuthentication { SupportsPasswordSignIn = false });
        context.Services.AddSingleton<IConsoleOidcAuthentication>(oidc);
        var page = context.Render<AuthSignInPage>();
        Assert.Empty(page.FindAll("input"));
        await page.Find("button").ClickAsync(new());
        Assert.Equal("entra", oidc.Provider);
        Assert.Equal("/operate/connections", oidc.ReturnTo);
        Assert.Equal("https://identity.example/authorize", ((MountedNavigation)context.Services.GetRequiredService<NavigationManager>()).ExternalDestination);
    }

    [Fact]
    public async Task ProviderDiscoveryTimeoutShowsRecoveryMessage()
    {
        await using var context = CreateContext();
        context.Services.AddSingleton<IConsoleOidcAuthentication>(new StubOidc { TimeoutAt = "discovery" });
        var page = context.Render<AuthSignInPage>();
        Assert.Contains("Identity providers could not be loaded", page.Markup);
    }

    [Fact]
    public async Task ProviderInitiationTimeoutAllowsRetry()
    {
        await using var context = CreateContext();
        context.Services.AddSingleton<IConsoleOidcAuthentication>(new StubOidc { TimeoutAt = "begin" });
        var page = context.Render<AuthSignInPage>();
        await page.Find("button").ClickAsync(new());
        Assert.Contains("Sign-in could not start", page.Markup);
        Assert.False(page.Find("button").HasAttribute("disabled"));
    }

    private sealed class StubOidc : IConsoleOidcAuthentication
    {
        public bool IsAvailable => true;
        public string? TimeoutAt { get; init; }
        public string? Provider { get; private set; }
        public string? ReturnTo { get; private set; }
        public Task<IReadOnlyList<ConsoleIdentityProvider>> GetProvidersAsync(CancellationToken cancellationToken = default) =>
            TimeoutAt == "discovery"
                ? Task.FromException<IReadOnlyList<ConsoleIdentityProvider>>(new TaskCanceledException())
                : Task.FromResult<IReadOnlyList<ConsoleIdentityProvider>>([new("entra", "Organization")]);
        public Task<Uri> BeginAsync(string providerKey, string returnTo, CancellationToken cancellationToken = default)
        {
            if (TimeoutAt == "begin") return Task.FromException<Uri>(new TaskCanceledException());
            Provider = providerKey;
            ReturnTo = returnTo;
            return Task.FromResult(new Uri("https://identity.example/authorize"));
        }

    }

    private static BunitContext CreateContext(MountedNavigation? navigation = null)
    {
        var context = new BunitContext();
        context.Services.AddSingleton<IConsoleEnvironmentProfileStore>(new InMemoryConsoleEnvironmentProfileStore([
            new ConsoleEnvironmentProfile { Id = "configured-api", DisplayName = "Honua" }
        ]));
        context.Services.AddSingleton<IConsoleAccountSessionStore>(new InMemoryConsoleAccountSessionStore());
        context.Services.AddSingleton<NavigationManager>(navigation ?? new MountedNavigation());
        return context;
    }

    private sealed class MountedNavigation : NavigationManager
    {
        public MountedNavigation() => Initialize("http://localhost/console/", "http://localhost/console/auth/signin?returnTo=%2Foperate%2Fconnections");
        public string? ExternalDestination { get; private set; }
        protected override void NavigateToCore(string uri, bool forceLoad)
        {
            var destination = ToAbsoluteUri(uri).AbsoluteUri;
            if (destination.StartsWith(BaseUri, StringComparison.Ordinal))
                Uri = destination;
            else
                ExternalDestination = destination;
        }
        protected override void NavigateToCore(string uri, NavigationOptions options) => NavigateToCore(uri, options.ForceLoad);
    }

    private sealed class PendingAuthentication : IConsoleBrowserAuthentication
    {
        public bool SupportsPasswordSignIn { get; init; } = true;
        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string? Password { get; private set; }
        public Task SignInAsync(string password, CancellationToken cancellationToken = default)
        {
            Password = password;
            return Completion.Task;
        }
    }
}
