using Bunit;
using Honua.Console.Shell;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.Layout;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

// Security gate: without a session no protected route renders, and the shell shows no menu or inventory.
public sealed class ConsoleSignInGateTests
{
    public static TheoryData<Type, string> ProtectedRoutes()
    {
        var routes = new TheoryData<Type, string>();
        var probes = ConsoleRouteAuthorizationAuditTests.RoutableComponents()
            .Where(component => !component.AllowsAnonymous)
            .SelectMany(component => component.Templates.Select(template => (component.Type, template)));
        foreach (var (type, template) in probes)
        {
            routes.Add(type, ConsoleRouteAuthorizationAuditTests.ToProbePath(template).Value!);
        }

        return routes;
    }

    [Theory]
    [MemberData(nameof(ProtectedRoutes))]
    public void AnonymousVisitToAProtectedRouteRedirectsToSignInWithoutTheMenu(Type pageType, string route)
    {
        using var context = CreateContext();
        context.AddAuthorization().SetNotAuthorized();
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo(route);

        // No page services are registered: creating the page would throw.
        var view = context.Render<AuthorizeRouteView>(parameters => parameters
            .Add(component => component.RouteData, new RouteData(pageType, new Dictionary<string, object?>()))
            .Add(component => component.DefaultLayout, typeof(ConsoleLayout))
            .Add(component => component.NotAuthorized, _ => builder =>
            {
                builder.OpenComponent<RedirectToLogin>(0);
                builder.CloseComponent();
            }));

        Assert.StartsWith("http://localhost/auth/login?returnTo=", navigation.Uri, StringComparison.Ordinal);
        Assert.Empty(view.FindAll(".console-topnav, .console-contextbar, [data-publication-navigation]"));
    }

    [Fact]
    public void SignedOutShellShowsOnlyTheProductNameAndThePublicPage()
    {
        using var context = CreateContext();
        context.AddAuthorization().SetNotAuthorized();
        context.Services.AddSingleton<IConsoleCapabilityManifest>(new PublicManifest());
        context.Services.AddSingleton<IConsoleBrowserAuthentication>(new SignedOutBrowserAuthentication());

        var shell = context.Render<ConsoleLayout>(parameters => parameters.Add(layout => layout.Body, builder =>
        {
            builder.OpenComponent<CatalogAvailabilityGate>(0);
            builder.AddAttribute(1, nameof(CatalogAvailabilityGate.ChildContent),
                (RenderFragment)(child => child.AddMarkupContent(0, "<p id='public-body'>Public layer</p>")));
            builder.CloseComponent();
        }));

        Assert.Equal("true", shell.Find(".console-shell").GetAttribute("data-console-signed-out"));
        Assert.Equal("Honua Console", shell.Find(".console-topbar-brand").TextContent.Trim());
        Assert.Single(shell.FindAll("[data-console-signin]"));
        Assert.Empty(shell.FindAll("nav, a[href]:not(.console-skip-link):not([data-console-signin]), .console-contextbar, [data-publication-navigation]"));
        // The capability manifest is a public endpoint; public pages need it to decide availability.
        shell.WaitForAssertion(() => Assert.Single(shell.FindAll("#public-body")));
    }

    [Fact]
    public void SignedInShellKeepsTheMenu()
    {
        using var context = CreateContext();
        context.AddAuthorization().SetAuthorized("synthetic-operator");

        var shell = context.Render<ConsoleLayout>();

        shell.WaitForAssertion(() => Assert.NotEmpty(shell.FindAll(".console-topnav a")));
        Assert.Null(shell.Find(".console-shell").GetAttribute("data-console-signed-out"));
    }

    [Fact]
    public void SigningInKeepsTheSamePageInstance()
    {
        using var context = CreateContext();
        var authorization = context.AddAuthorization();
        authorization.SetNotAuthorized();
        var shell = context.Render<ConsoleLayout>(parameters => parameters.Add(layout => layout.Body, builder =>
        {
            builder.OpenComponent<InstanceProbe>(0);
            builder.CloseComponent();
        }));
        var before = shell.FindComponent<InstanceProbe>().Instance;

        authorization.SetAuthorized("synthetic-operator");

        shell.WaitForAssertion(() => Assert.NotEmpty(shell.FindAll(".console-topnav a")));
        Assert.Same(before, shell.FindComponent<InstanceProbe>().Instance);
    }

    private static BunitContext CreateContext()
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.AddConsoleNotifications();
        context.Services.AddSingleton(new CatalogAdminOptions());
        context.Services.AddSingleton<ConsoleAgentContextRegistry>();
        context.Services.AddSingleton<IConsoleProductMode>(new ConfiguredConsoleProductMode(ConsoleProductMode.Full));
        context.Services.AddSingleton<IConsoleCapabilityManifest>(ConsoleCapabilityManifest.FromConfigurationList(null));
        return context;
    }

    private sealed class InstanceProbe : ComponentBase;

    private sealed class SignedOutBrowserAuthentication : IConsoleBrowserAuthentication
    {
        public bool SupportsPasswordSignIn => true;

        public Task<Uri?> SignOutAsync(CancellationToken cancellationToken = default) => Task.FromResult<Uri?>(null);

        public Task SignInAsync(string password, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class PublicManifest : IConsoleCapabilityManifest
    {
        private bool _read;

        public event Action? Changed;

        public Task RefreshAsync(CancellationToken cancellationToken = default)
        {
            _read = true;
            Changed?.Invoke();
            return Task.CompletedTask;
        }

        public bool IsAdvertised(string capabilityKey) => _read && capabilityKey == ConsoleCapabilityKeys.PersistentCatalog;
    }
}
