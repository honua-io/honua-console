using System.Security.Claims;
using Honua.Console.Shell.Layout;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Honua.Console.Shell.Tests;

public sealed class ConsolePublicationNavigationDisposalTests
{
    [Fact]
    public Task AuthenticatedNavigationCanDisposeAfterItsServiceScopeCloses() =>
        DisposeAfterServiceScopeAsync(new Authentication());

    [Fact]
    public Task NavigationWithoutAuthenticationCanDisposeAfterItsServiceScopeCloses() =>
        DisposeAfterServiceScopeAsync(null);

    private static async Task DisposeAfterServiceScopeAsync(AuthenticationStateProvider? authentication)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton<NavigationManager>(new Navigation());
        if (authentication is not null) services.AddSingleton(authentication);
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var component = await renderer.RenderComponentAsync<ConsolePublicationNavigation>(ParameterView.Empty);
            Assert.Contains("Service navigation", component.ToHtmlString());
        });
        await provider.DisposeAsync();
        await renderer.DisposeAsync();
    }

    private sealed class Navigation : NavigationManager
    {
        public Navigation() => Initialize("https://console.example/", "https://console.example/operate/publishing");
        protected override void NavigateToCore(string uri, bool forceLoad) => throw new NotSupportedException();
    }

    private sealed class Authentication : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
            Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity())));
    }
}
