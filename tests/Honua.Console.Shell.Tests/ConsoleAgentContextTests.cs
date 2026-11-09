using Bunit;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

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
            Entities = entities, Selection = entities, Filters = filters,
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
    public void Page_component_obeys_flag_and_unregisters_on_dismount(bool enabled)
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
            cut.Render(parameters => parameters.Add(component => component.Context, new() { Route = "/next", Entities = [Folder("next")] }));
            Assert.Equal("next", Assert.Single(registry.Current.Entities).Id);
        }
        cut.Dispose();
        Assert.Empty(registry.Current.Entities);
        Assert.Empty(cut.Markup);
    }

    private static ConsoleAgentEntity Folder(string id) => new() { Kind = ConsoleAgentEntityKind.Folder, Id = id };
}
