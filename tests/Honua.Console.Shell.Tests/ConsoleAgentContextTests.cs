using Bunit;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace Honua.Console.Shell.Tests;

public sealed class ConsoleAgentContextTests
{
    [Fact]
    public void Snapshot_is_atomic_and_defensively_copies_collections()
    {
        var registry = new ConsoleAgentContextRegistry();
        var entities = new List<ConsoleAgentEntity> { Folder("stable-id") };
        var filters = new Dictionary<string, string> { ["status"] = "active" };
        using var registration = registry.Register(new ConsoleAgentPageContext
        {
            Route = "/operate/folders/stable-id?secret=excluded",
            Entities = entities,
            Selection = entities,
            Filters = filters,
            Unsaved = new() { ChangedFieldCount = 2 }
        });
        entities.Clear();
        filters["status"] = "changed";
        var snapshot = registry.Current;
        Assert.Equal("/operate/folders/stable-id", snapshot.Route);
        Assert.Equal("stable-id", Assert.Single(snapshot.Entities).Id);
        Assert.Equal(snapshot.Entities, snapshot.Selection);
        Assert.Equal("active", snapshot.Filters["status"]);
        Assert.Equal(2, snapshot.Unsaved.ChangedFieldCount);
        Assert.Equal(1, snapshot.Generation);
    }

    [Fact]
    public void Replaced_and_disposed_registrations_cannot_overwrite_new_page()
    {
        var registry = new ConsoleAgentContextRegistry();
        var old = registry.Register(new() { Route = "/old" });
        using var current = registry.Register(new() { Route = "/new" });
        old.Publish(new() { Route = "/stale" });
        old.Dispose();
        Assert.False(old.IsActive);
        Assert.True(current.IsActive);
        Assert.Equal("/new", registry.Current.Route);
        registry.Navigate("/next");
        current.Publish(new() { Route = "/stale" });
        Assert.False(current.IsActive);
        Assert.Equal("/next", registry.Current.Route);
        Assert.Empty(registry.Current.Entities);
    }

    [Fact]
    public void Only_real_changes_advance_generation_including_dirty_counts()
    {
        var registry = new ConsoleAgentContextRegistry();
        var context = new ConsoleAgentPageContext { Route = "/page" };
        using var registration = registry.Register(context);
        var generation = registry.Current.Generation;
        registration.Publish(context with { Filters = new Dictionary<string, string>() });
        Assert.Equal(generation, registry.Current.Generation);
        registration.Publish(context with { Unsaved = new() { PendingOperationCount = 1 } });
        Assert.Equal(generation + 1, registry.Current.Generation);
        registration.Dispose();
        Assert.Equal(generation + 2, registry.Current.Generation);
        Assert.Equal(0, registry.Current.Unsaved.PendingOperationCount);
    }

    [Fact]
    public void Router_render_before_bridge_navigation_preserves_destination_context()
    {
        var registry = new ConsoleAgentContextRegistry();
        using var old = registry.Register(new() { Route = "/old" });
        using var destination = registry.Register(new()
        {
            Route = "/next",
            Entities = [Folder("next")]
        });
        var generation = registry.Current.Generation;
        registry.Navigate("/next?filter=active");
        Assert.True(destination.IsActive);
        Assert.Equal("next", Assert.Single(registry.Current.Entities).Id);
        Assert.Equal(generation, registry.Current.Generation);
        registry.Invalidate("/next");
        Assert.False(destination.IsActive);
        Assert.Empty(registry.Current.Entities);
        Assert.Equal(generation + 1, registry.Current.Generation);
    }

    [Theory]
    [InlineData(ConsoleAgentEntityKind.Folder, "/operate/folders/a%20b")]
    [InlineData(ConsoleAgentEntityKind.Service, "/operate/services/a%20b/settings")]
    [InlineData(ConsoleAgentEntityKind.Layer, "/operate/layers/a%20b")]
    [InlineData(ConsoleAgentEntityKind.Job, "/operate/jobs/a%20b")]
    [InlineData(ConsoleAgentEntityKind.Approval, "/inbox?proposalId=a%20b")]
    [InlineData(ConsoleAgentEntityKind.Item, "/share/manage?itemId=a%20b")]
    public void Typed_routes_use_encoded_stable_ids(ConsoleAgentEntityKind kind, string expected)
    {
        Assert.Equal(expected, ConsoleAgentRoutes.ForEntity(new() { Kind = kind, Id = "a b" }));
    }

    [Fact]
    public void Canonical_layer_and_wire_service_identity_take_precedence()
    {
        Assert.Equal("/operate/layers/connection%3A12", ConsoleAgentRoutes.ForEntity(new()
        { Kind = ConsoleAgentEntityKind.Layer, Id = "12", ResourceId = "connection:12" }));
        Assert.Equal("/operate/services/wire%2Fname/settings", ConsoleAgentRoutes.ForEntity(new()
        { Kind = ConsoleAgentEntityKind.Service, Id = "opaque-id", ServiceName = "wire/name" }));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("..")]
    [InlineData("bad\nvalue")]
    public void Invalid_entity_identity_is_rejected(string id)
    {
        Assert.ThrowsAny<ArgumentException>(() => ConsoleAgentRoutes.ForEntity(Folder(id)));
    }

    [Theory]
    [InlineData("https://other.example")]
    [InlineData("//other.example")]
    [InlineData("/bad\\route")]
    public void Registry_rejects_nonlocal_routes(string route)
    {
        var registry = new ConsoleAgentContextRegistry();
        Assert.Throws<ArgumentException>(() => registry.Register(new() { Route = route }));
        Assert.Equal(0, registry.Current.Generation);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Page_component_obeys_flag_and_unregisters_on_dismount(bool enabled)
    {
        using var renderer = new BunitContext();
        var registry = new ConsoleAgentContextRegistry();
        renderer.Services.AddSingleton(registry);
        renderer.Services.AddSingleton(new ConsoleV4Options { Enabled = enabled });
        var cut = renderer.Render<ConsoleAgentContext>(parameters => parameters.Add(component => component.Context,
            new() { Route = "/folder", Entities = [Folder("selected")] }));
        Assert.Equal(enabled ? "/folder" : "/", registry.Current.Route);
        Assert.Equal(enabled ? 1 : 0, registry.Current.Entities.Count);
        if (enabled)
        {
            registry.Navigate("/next");
            cut.Render(parameters => parameters.Add(component => component.Context, new()
            {
                Route = "/next",
                Entities = [Folder("next")]
            }));
            Assert.Equal("next", Assert.Single(registry.Current.Entities).Id);
        }
        Assert.Empty(cut.Markup);
        await renderer.DisposeComponentsAsync();
        Assert.Empty(registry.Current.Entities);
    }

    [Fact]
    public async Task Bridge_preserves_router_publication_and_detaches_on_disposal()
    {
        using var renderer = new BunitContext();
        var registry = new ConsoleAgentContextRegistry();
        renderer.Services.AddSingleton(registry);
        renderer.Services.AddSingleton(new ConsoleV4Options { Enabled = true });
        var module = renderer.JSInterop.SetupModule("./js/console-agent-context.mjs");
        module.SetupVoid("install", _ => true).SetVoidResult();
        module.SetupVoid("publish", _ => true).SetVoidResult();
        module.SetupVoid("uninstall", _ => true).SetVoidResult();
        var navigation = renderer.Services.GetRequiredService<NavigationManager>();
        ConsoleAgentContextRegistry.Registration? destination = null;
        navigation.LocationChanged += (_, _) => destination = registry.Register(new()
        {
            Route = "/next",
            Entities = [Folder("next")]
        });
        var cut = renderer.Render<ConsoleAgentContextBridge>();
        cut.WaitForAssertion(() => Assert.Single(module.Invocations["install"]));
        await cut.InvokeAsync(() => navigation.NavigateTo("/next"));
        Assert.True(destination!.IsActive);
        Assert.Equal("next", Assert.Single(registry.Current.Entities).Id);
        cut.WaitForAssertion(() => Assert.NotEmpty(module.Invocations["publish"]));
        await renderer.DisposeComponentsAsync();
        Assert.Single(module.Invocations["uninstall"]);
        await cut.InvokeAsync(() => navigation.NavigateTo("/other"));
        Assert.Equal("/next", registry.Current.Route);
    }

    [Fact]
    public async Task Bridge_disposed_during_import_never_installs_browser_provider()
    {
        using var renderer = new BunitContext();
        var runtime = new PendingImport();
        renderer.Services.AddSingleton<IJSRuntime>(runtime);
        renderer.Services.AddSingleton(new ConsoleAgentContextRegistry());
        renderer.Services.AddSingleton(new ConsoleV4Options { Enabled = true });
        renderer.Render<ConsoleAgentContextBridge>();
        await runtime.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await renderer.DisposeComponentsAsync();
        runtime.Result.SetResult(runtime);
        await runtime.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Empty(runtime.ModuleCalls);
    }

    private sealed class PendingImport : IJSRuntime, IJSObjectReference
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<IJSObjectReference> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<string> ModuleCalls { get; } = [];

        ValueTask<TValue> IJSRuntime.InvokeAsync<TValue>(string identifier, object?[]? args) => Import<TValue>();
        ValueTask<TValue> IJSRuntime.InvokeAsync<TValue>(string identifier, CancellationToken token, object?[]? args) => Import<TValue>();
        private async ValueTask<TValue> Import<TValue>()
        {
            Started.TrySetResult();
            return (TValue)(object)await Result.Task;
        }
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            ModuleCalls.Add(identifier);
            return ValueTask.FromResult(default(TValue)!);
        }
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken token, object?[]? args) => InvokeAsync<TValue>(identifier, args);
        public ValueTask DisposeAsync()
        {
            Disposed.TrySetResult();
            return ValueTask.CompletedTask;
        }
    }

    private static ConsoleAgentEntity Folder(string id) => new() { Kind = ConsoleAgentEntityKind.Folder, Id = id };
}
