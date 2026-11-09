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
