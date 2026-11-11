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
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task ThemeUsesTheSingleV4Flag(bool flag, bool enabled)
    {
        await using var context = CreateContext(new ConsoleV4Options { Enabled = flag });
        context.Render<ConsoleTheme>();
        Assert.Equal(enabled, context.JSInterop.Invocations.Single().Arguments[0]);
        Assert.Equal(enabled, context.JSInterop.Invocations.Single().Arguments[1] is string);
    }

    [Fact]
    public async Task AccountChangesAndSignOutReconfigureThePreference()
    {
        await using var context = CreateContext(new ConsoleV4Options { Enabled = true });
        var authentication = context.Services.GetRequiredService<AuthenticationSignals>();
        var component = context.Render<ConsoleTheme>();
        var firstKey = Assert.IsType<string>(context.JSInterop.Invocations.Last().Arguments[1]);
        Assert.DoesNotContain("operator-a", firstKey, StringComparison.Ordinal);
        await component.InvokeAsync(() => authentication.SignIn("operator-b"));
        component.WaitForAssertion(() => Assert.NotEqual(firstKey, context.JSInterop.Invocations.Last().Arguments[1]));
        await component.InvokeAsync(() => authentication.SignIn(null));
        component.WaitForAssertion(() => Assert.Null(context.JSInterop.Invocations.Last().Arguments[1]));
        await component.InvokeAsync(() => authentication.SignIn("operator-a"));
        component.WaitForAssertion(() => Assert.Equal(firstKey, context.JSInterop.Invocations.Last().Arguments[1]));
    }

    [Fact]
    public async Task EmbedNavigationDisablesThemeAndReturningRestoresTheScope()
    {
        await using var context = CreateContext(new ConsoleV4Options { Enabled = true });
        var component = context.Render<ConsoleTheme>();
        var key = context.JSInterop.Invocations.Last().Arguments[1];
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        await component.InvokeAsync(() => navigation.NavigateTo("/console/embed/example"));
        component.WaitForAssertion(() => Assert.Equal(false, context.JSInterop.Invocations.Last().Arguments[0]));
        await component.InvokeAsync(() => navigation.NavigateTo("/console/"));
        component.WaitForAssertion(() => Assert.Equal(key, context.JSInterop.Invocations.Last().Arguments[1]));
    }

    private static BunitContext CreateContext(ConsoleV4Options options)
    {
        var context = new BunitContext();
        context.Services.AddSingleton<AuthenticationSignals>();
        context.Services.AddSingleton<AuthenticationStateProvider>(provider => provider.GetRequiredService<AuthenticationSignals>());
        context.Services.AddSingleton(options);
        context.Services.AddSingleton(new ConsoleApiClient(new HttpClient { BaseAddress = new Uri("https://api.example.test/") }));
        context.JSInterop.SetupVoid("honuaConsoleTheme.configure", _ => true).SetVoidResult();
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
