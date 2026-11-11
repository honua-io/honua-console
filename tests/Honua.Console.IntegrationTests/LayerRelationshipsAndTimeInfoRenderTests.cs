using Bunit;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class LayerRelationshipsAndTimeInfoRenderTests
{
    private const string ResourceId = "conn-1-layer-1";

    // ---- Relationships editor ----

    [Fact]
    public void Relationships_WhenBound_RendersExistingRowsFromGet()
    {
        var page = RenderRelationships(new FakeRelationships
        {
            Read = new ConsoleLayerRelationships
            {
                Bound = true,
                LayerId = 1,
                Relationships =
                [
                    new ConsoleLayerRelationship
                    {
                        Id = "rel0", Name = "self", RelatedLayerId = 1, Role = "origin",
                        Cardinality = "one-to-many", OriginField = "id", DestinationField = "id", EsriRelationshipId = 7,
                    }
                ],
            },
        });

        page.WaitForAssertion(
            () => Assert.Contains("data-relationship-row", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Contains("value=\"self\"", page.Markup, StringComparison.Ordinal);
        Assert.Contains("data-rel-save", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Relationships_AddRowThenSave_IssuesSaveWithTheRows()
    {
        var fake = new FakeRelationships
        {
            Read = new ConsoleLayerRelationships { Bound = true, LayerId = 1, Relationships = [] },
            SaveResult = new ConsoleSetRelationshipsResult { Succeeded = true, State = "Updated", Detail = "Saved 1 relationship(s) on this layer." },
        };
        var page = RenderRelationships(fake);

        page.WaitForAssertion(
            () => Assert.Contains("data-relationships-empty", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        page.Find("[data-rel-add]").Click();
        page.Find("[data-rel-save]").Click();

        page.WaitForAssertion(
            () => Assert.Contains("data-relationships-result", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        // The page issued a real save call carrying the (one) added row.
        Assert.NotNull(fake.LastSaved);
        Assert.Single(fake.LastSaved!);
        Assert.Contains("Saved 1 relationship", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Relationships_MergedBuildPage_RendersMissingBindingThroughRealDi()
    {
        using var ctx = new Bunit.BunitContext();
        ctx.AddConsoleNotifications();
        ctx.Services.AddSingleton<IOperateTransitionDataSource>(LayerData());
        ctx.Services.AddSingleton<IConsoleLayerRelationshipsOperation, UnsupportedConsoleLayerRelationshipsOperation>();

        var page = ctx.Render<OperateLayerRelationshipsPage>(p => p.Add(x => x.ResourceId, ResourceId));

        page.WaitForAssertion(
            () => Assert.Contains("data-relationships-unbound", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Contains("HONUA_SERVER_BASE_URL", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void TimeInfo_Unsupported_HasNoWriteControls()
    {
        using var ctx = new BunitContext();
        var component = ctx.Render<ServiceTimeInfoSetter>();
        Assert.Contains("Unsupported", component.Markup, StringComparison.Ordinal);
        Assert.Contains("Dataset time-field", component.Markup, StringComparison.Ordinal);
        Assert.Empty(component.FindAll("input, button, textarea"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Relationships_ContextCountsDraftAndPreservesItOnRerender(bool enabled)
    {
        using var ctx = new BunitContext();
        ctx.AddConsoleNotifications();
        var registry = new ConsoleAgentContextRegistry();
        ctx.Services.AddSingleton(registry);
        ctx.Services.AddSingleton(new ConsoleV4Options { Enabled = enabled });
        ctx.Services.AddSingleton<Microsoft.AspNetCore.Components.NavigationManager>(new MountedNavigationManager("/console/"));
        ctx.Services.AddSingleton<IOperateTransitionDataSource>(LayerData());
        ctx.Services.AddSingleton<IConsoleLayerRelationshipsOperation>(new FakeRelationships
        {
            Read = new() { Bound = true, LayerId = 1, Relationships = [] }
        });
        var page = ctx.Render<OperateLayerRelationshipsPage>(p => p.Add(x => x.ResourceId, ResourceId));
        page.WaitForAssertion(() => page.Find("[data-rel-add]"));
        Assert.Equal(enabled, page.FindAll(".console-page-heading-v4").Count == 1);
        Assert.Equal("/console/operate/layers/conn-1-layer-1", page.Find("[data-layer-detail-link]").GetAttribute("href"));
        if (enabled)
        {
            Assert.Equal(ResourceId, Assert.Single(registry.Current.Selection).ResourceId);
            Assert.Equal("1", Assert.Single(registry.Current.Entities).Id);
        }
        if (enabled) CatalogAdminPreview.Capture("data-layer-relationships-empty", page.Markup);
        page.Find("[data-rel-add]").Click();
        page.Find("[data-rel-name]").Change("private_relationship_name");
        page.WaitForAssertion(() => Assert.Equal(enabled ? 1 : 0, registry.Current.Unsaved.ChangedFieldCount));
        Assert.DoesNotContain("private_relationship_name", System.Text.Json.JsonSerializer.Serialize(registry.Current));
        page.Render(p => p.Add(x => x.ResourceId, ResourceId));
        Assert.Equal("private_relationship_name", page.Find("[data-rel-name]").GetAttribute("value"));
        if (enabled) CatalogAdminPreview.Capture("data-layer-relationships-draft", page.Markup);
        page.Find("[data-rel-remove]").Click();
        Assert.Equal(0, registry.Current.Unsaved.ChangedFieldCount);
    }

    private static IRenderedComponent<OperateLayerRelationshipsPage> RenderRelationships(FakeRelationships relationships)
    {
        var ctx = new Bunit.BunitContext();
        ctx.AddConsoleNotifications();
        ctx.Services.AddSingleton<IOperateTransitionDataSource>(LayerData());
        ctx.Services.AddSingleton<IConsoleLayerRelationshipsOperation>(relationships);
        return ctx.Render<OperateLayerRelationshipsPage>(p => p.Add(x => x.ResourceId, ResourceId));
    }

    private static FixedServicesDataSource LayerData() =>
        FixedServicesDataSource.WithLayer(new OperateServiceLayerProjection(1, "Parcels", "polygon", ResourceId, "parcels"));

    private sealed class FakeRelationships : IConsoleLayerRelationshipsOperation
    {
        public ConsoleLayerRelationships Read { get; set; } = ConsoleLayerRelationships.Unbound("test");
        public ConsoleSetRelationshipsResult? SaveResult { get; set; }
        public IReadOnlyList<ConsoleLayerRelationship>? LastSaved { get; private set; }

        public Task<ConsoleLayerRelationships> GetRelationshipsAsync(int layerId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Read);

        public Task<ConsoleSetRelationshipsResult> SetRelationshipsAsync(
            int layerId, IReadOnlyList<ConsoleLayerRelationship> relationships, CancellationToken cancellationToken = default)
        {
            LastSaved = relationships;
            return Task.FromResult(SaveResult ?? new ConsoleSetRelationshipsResult { Succeeded = true, State = "Updated" });
        }
    }

}
