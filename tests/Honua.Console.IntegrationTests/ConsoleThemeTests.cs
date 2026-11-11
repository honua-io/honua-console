using System.Security.Claims;
using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Web.Components;
using Honua.Console.Web.Runtime;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class ConsoleThemeTests
{
    private const string Enable = "honuaConsoleTheme.enable";
    private const string Disable = "honuaConsoleTheme.disable";

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ThemeUsesTheSingleV4Flag(bool enabled)
    {
        await using var context = CreateContext(new ConsoleV4Options { Enabled = enabled });
        context.Render<ConsoleTheme>();
        var invocation = context.JSInterop.Invocations.Single();
        Assert.Equal(enabled ? Enable : Disable, invocation.Identifier);
        Assert.Equal(enabled, invocation.Arguments.SingleOrDefault() is string);
    }

    [Fact]
    public async Task SameAccountUsesSeparatePreferencesForEachApi()
    {
        await using var first = CreateContext(new ConsoleV4Options { Enabled = true });
        first.Render<ConsoleTheme>();
        await using var second = CreateContext(new ConsoleV4Options { Enabled = true });
        second.Services.AddSingleton(new ConsoleApiClient(new HttpClient { BaseAddress = new Uri("https://other-api.example.test/") }));
        second.Render<ConsoleTheme>();
        Assert.NotEqual(LastKey(first), LastKey(second));
    }

    [Fact]
    public async Task AccountChangesAndSignOutReconfigureThePreference()
    {
        await using var context = CreateContext(new ConsoleV4Options { Enabled = true });
        var authentication = context.Services.GetRequiredService<AuthenticationSignals>();
        var component = context.Render<ConsoleTheme>();
        var firstKey = Assert.IsType<string>(LastKey(context));
        Assert.DoesNotContain("operator-a", firstKey, StringComparison.Ordinal);
        await component.InvokeAsync(() => authentication.SignIn("operator-b"));
        component.WaitForAssertion(() => Assert.NotEqual(firstKey, LastKey(context)));
        await component.InvokeAsync(() => authentication.SignIn(null));
        component.WaitForAssertion(() => Assert.Null(LastKey(context)));
        await component.InvokeAsync(() => authentication.SignIn("operator-a"));
        component.WaitForAssertion(() => Assert.Equal(firstKey, LastKey(context)));
    }

    [Fact]
    public async Task EmbedNavigationDisablesThemeAndReturningRestoresTheScope()
    {
        await using var context = CreateContext(new ConsoleV4Options { Enabled = true });
        var component = context.Render<ConsoleTheme>();
        var key = LastKey(context);
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        await component.InvokeAsync(() => navigation.NavigateTo("/console/embed/example"));
        component.WaitForAssertion(() => Assert.Single(context.JSInterop.Invocations[Disable]));
        await component.InvokeAsync(() => navigation.NavigateTo("/console/"));
        component.WaitForAssertion(() => Assert.Equal(2, context.JSInterop.Invocations[Enable].Count));
        Assert.Equal(key, LastKey(context));
    }

    private static object? LastKey(BunitContext context) =>
        context.JSInterop.Invocations[Enable].Last().Arguments.Single();

    private static BunitContext CreateContext(ConsoleV4Options options)
    {
        var context = new BunitContext();
        context.Services.AddSingleton<AuthenticationSignals>();
        context.Services.AddSingleton<AuthenticationStateProvider>(provider => provider.GetRequiredService<AuthenticationSignals>());
        context.Services.AddSingleton(options);
        context.Services.AddSingleton(new ConsoleApiClient(new HttpClient { BaseAddress = new Uri("https://api.example.test/") }));
        context.JSInterop.SetupVoid(Enable, _ => true).SetVoidResult();
        context.JSInterop.SetupVoid(Disable, _ => true).SetVoidResult();
        return context;
    }

    private sealed class AuthenticationSignals : AuthenticationStateProvider
    {
        private ClaimsPrincipal _user = User("operator-a");
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(_user));
        public void SignIn(string? id)
        {
            _user = User(id);
            NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
        }
        private static ClaimsPrincipal User(string? id) => new(id is null
            ? new ClaimsIdentity()
            : new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id)], "test"));
    }
}
