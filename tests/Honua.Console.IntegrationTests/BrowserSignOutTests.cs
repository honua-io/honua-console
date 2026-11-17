using Bunit;
using Honua.Console.Shell.Layout;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class BrowserSignOutTests
{
    [Fact]
    public void AuthenticatedOperatorHasAVisibleAccountAndSignOutOutsideCollapsedNavigation()
    {
        using var context = CreateContext(new BrowserAuthentication());
        var navigation = context.Render<ConsoleTopNavigation>();

        var account = navigation.Find("header > .console-account");
        Assert.Contains("Alice Operator", account.TextContent);
        Assert.Equal("Sign out", account.QuerySelector("button")!.TextContent.Trim());
    }

    [Fact]
    public void AnonymousBrowserDoesNotShowAnAccountControl()
    {
        using var context = CreateContext(new BrowserAuthentication());
        context.AddAuthorization().SetNotAuthorized();

        Assert.Empty(context.Render<ConsoleAccount>().FindAll("button"));
    }

    [Fact]
    public async Task PendingSignOutDisablesTheControlAndReloadsMountedSignInAfterSuccess()
    {
        var authentication = new BrowserAuthentication();
        using var context = CreateContext(authentication);
        var navigation = new MountedNavigation();
        context.Services.AddSingleton<NavigationManager>(navigation);
        var account = context.Render<ConsoleAccount>();

        var click = account.Find("button").ClickAsync(new());
        account.WaitForAssertion(() => Assert.True(account.Find("button").HasAttribute("disabled")));
        Assert.Equal(1, authentication.Calls);
        Assert.False(navigation.ForceLoad);
        authentication.Completion.SetResult();
        await click;

        Assert.True(navigation.ForceLoad);
        Assert.Equal("http://localhost/console/auth/signin", navigation.Uri);
    }

    [Fact]
    public async Task FailedSignOutShowsSafeRetryAndKeepsTheCurrentPage()
    {
        var authentication = new BrowserAuthentication();
        using var context = CreateContext(authentication);
        var account = context.Render<ConsoleAccount>();
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        var original = navigation.Uri;

        var click = account.Find("button").ClickAsync(new());
        authentication.Completion.SetException(new HttpRequestException("private server detail"));
        await click;

        Assert.Equal("Sign out failed. Try again.", account.Find("[role=alert]").TextContent);
        Assert.DoesNotContain("private server detail", account.Markup);
        Assert.False(account.Find("button").HasAttribute("disabled"));
        Assert.Equal(original, navigation.Uri);
        authentication.Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var retry = account.Find("button").ClickAsync(new());
        Assert.Equal(2, authentication.Calls);
        authentication.Completion.SetException(new HttpRequestException());
        await retry;
    }

    private static BunitContext CreateContext(BrowserAuthentication authentication)
    {
        var context = new BunitContext();
        context.AddAuthorization().SetAuthorized("Alice Operator");
        context.Services.AddSingleton<IConsoleBrowserAuthentication>(authentication);
        return context;
    }

    private sealed class MountedNavigation : NavigationManager
    {
        public MountedNavigation() => Initialize("http://localhost/console/", "http://localhost/console/operate");
        public bool ForceLoad { get; private set; }
        protected override void NavigateToCore(string uri, bool forceLoad)
        {
            ForceLoad = forceLoad;
            Uri = ToAbsoluteUri(uri).AbsoluteUri;
        }
    }

    private sealed class BrowserAuthentication : IConsoleBrowserAuthentication
    {
        public bool SupportsPasswordSignIn => true;
        public int Calls { get; private set; }
        public TaskCompletionSource Completion { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task SignInAsync(string password, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SignOutAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            return Completion.Task;
        }
    }
}
