using Bunit;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.Shell.Tests;

public sealed class ConsoleAgentContextMountTests
{
    [Theory]
    [InlineData("/")]
    [InlineData("/console/")]
    [InlineData("/apps/console/")]
    public async Task Query_navigation_preserves_context_and_dirty_updates_under_mount(string mount)
    {
        using var renderer = new BunitContext();
        const string route = "operate/services/service%3Aone/settings";
        var navigation = new HostedNavigationManager("https://honua.example" + mount, route);
        var registry = new ConsoleAgentContextRegistry();
        renderer.Services.AddSingleton<NavigationManager>(navigation);
        renderer.Services.AddSingleton(registry);
        renderer.Services.AddSingleton(new ConsoleV4Options { Enabled = true });
        var module = renderer.JSInterop.SetupModule("./js/console-agent-context.mjs");
        module.SetupVoid("install", _ => true).SetVoidResult();
        module.SetupVoid("publish", _ => true).SetVoidResult();
        module.SetupVoid("uninstall", _ => true).SetVoidResult();
        var entity = new ConsoleAgentEntity { Kind = ConsoleAgentEntityKind.Service, Id = "service:one" };
        var context = new ConsoleAgentPageContext
        {
            Route = mount + route,
            Entities = [entity],
            Selection = [entity],
            Unsaved = new() { ChangedFieldCount = 1 }
        };
        var publisher = renderer.Render<ConsoleAgentContext>(p => p.Add(c => c.Context, context));
        var generation = registry.Current.Generation;
        var bridge = renderer.Render<ConsoleAgentContextBridge>();
        await bridge.InvokeAsync(() => navigation.NavigateTo("./" + route + "?tab=details#editor"));
        Assert.Equal("/" + route, registry.Current.Route);
        Assert.Equal(generation, registry.Current.Generation);
        Assert.Equal(entity, Assert.Single(registry.Current.Selection));
        Assert.Equal(1, registry.Current.Unsaved.ChangedFieldCount);

        publisher.Render(p => p.Add(c => c.Context, context with
        {
            Route = new Uri(navigation.Uri).AbsolutePath,
            Unsaved = new() { ChangedFieldCount = 2, PendingOperationCount = 1 }
        }));
        Assert.Equal(generation + 1, registry.Current.Generation);
        Assert.Equal(2, registry.Current.Unsaved.ChangedFieldCount);
        Assert.Equal(1, registry.Current.Unsaved.PendingOperationCount);
        await bridge.InvokeAsync(() => navigation.NavigateTo("./inbox"));
        Assert.Equal("/inbox", registry.Current.Route);
        Assert.Empty(registry.Current.Entities);
        Assert.Equal(0, registry.Current.Unsaved.ChangedFieldCount);
    }

    [Theory]
    [InlineData("https://honua.example/console", "/")]
    [InlineData("https://honua.example/console/inbox?q=C:\\x", "/inbox")]
    [InlineData("https://honua.example/console/operate/services/a%2F..%2Fb", "/operate/services/a%2F..%2Fb")]
    public async Task Bridge_accepts_current_browser_locations_without_destination_validation(string location, string expected)
    {
        using var renderer = new BunitContext();
        var navigation = new HostedNavigationManager("https://honua.example/console/", "");
        navigation.NavigateTo(location);
        var registry = new ConsoleAgentContextRegistry();
        renderer.Services.AddSingleton<NavigationManager>(navigation);
        renderer.Services.AddSingleton(registry);
        renderer.Services.AddSingleton(new ConsoleV4Options { Enabled = true });
        var module = renderer.JSInterop.SetupModule("./js/console-agent-context.mjs");
        module.SetupVoid("install", _ => true).SetVoidResult();
        module.SetupVoid("publish", _ => true).SetVoidResult();
        module.SetupVoid("uninstall", _ => true).SetVoidResult();
        var bridge = renderer.Render<ConsoleAgentContextBridge>();
        Assert.Equal(expected, registry.Current.Route);
        await bridge.InvokeAsync(() => navigation.NavigateTo("./inbox?q=C:\\x"));
        Assert.Equal("/inbox", registry.Current.Route);
    }

    private sealed class HostedNavigationManager : NavigationManager
    {
        public HostedNavigationManager(string baseUri, string route) => Initialize(baseUri, baseUri + route);

        protected override void NavigateToCore(string uri, NavigationOptions options)
        {
            Uri = ToAbsoluteUri(uri).AbsoluteUri;
            NotifyLocationChanged(false);
        }
    }
}
