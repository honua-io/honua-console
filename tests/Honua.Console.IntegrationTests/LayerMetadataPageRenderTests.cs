using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class LayerMetadataPageRenderTests
{
    private const string ResourceId = "conn-1-layer-1";

    private static readonly OperateServiceLayerProjection Parcels =
        new(1, "Parcels", "polygon", ResourceId, "parcels");

    [Fact]
    public void Page_OffersOnlyTheEditingPolicy()
    {
        var page = RenderPage(new RecordingMetadata());

        page.WaitForAssertion(() => Assert.NotNull(page.Find("[data-editing-save]")));
        Assert.Empty(page.FindAll("[data-display-panel], [data-spatial-panel], [data-editing-creator], [data-editing-related]"));
    }

    [Fact]
    public void Save_SendsOnlyTheChangedFieldsForTheChosenService()
    {
        var metadata = new RecordingMetadata();
        var page = RenderPage(metadata, Service("zoning"), Service("cadastre"));
        page.WaitForAssertion(() => Assert.NotNull(page.Find("[data-editing-save]")));

        page.Find("[data-editing-service]").Change("zoning");
        page.Find("[data-editing-globalid]").Change(" globalid ");
        page.Find("[data-editing-delete]").Change("false");
        page.Find("[data-editing-save]").Click();

        page.WaitForAssertion(() => Assert.NotNull(page.Find("[data-editing-result]")));
        var change = Assert.Single(metadata.Changes);
        Assert.Equal(new ConsoleLayerEditingChange
        {
            ServiceName = "zoning",
            LayerId = 1,
            GlobalIdField = "globalid",
            AllowDelete = false,
        }, change);
    }

    [Fact]
    public void Save_ShowsTheServerOutcome()
    {
        var metadata = new RecordingMetadata
        {
            Result = new ConsoleSetLayerMetadataResult { Succeeded = false, State = "Rejected", Detail = "globalIdField must name a UUID field" },
        };
        var page = RenderPage(metadata);
        page.WaitForAssertion(() => Assert.NotNull(page.Find("[data-editing-save]")));

        page.Find("[data-editing-save]").Click();

        page.WaitForAssertion(() =>
            Assert.Contains("globalIdField must name a UUID field", page.Find("[data-editing-result]").TextContent, StringComparison.Ordinal));
    }

    [Fact]
    public void OnlyFeatureServerServicesAreOffered()
    {
        var page = RenderPage(new RecordingMetadata(),
            Service("a-map", "MapServer"), Service("b-mixed", "MapServer, FeatureServer"), Service("c-feature"));
        page.WaitForAssertion(() => Assert.NotNull(page.Find("[data-editing-save]")));

        var options = page.FindAll("[data-editing-service] option").Select(option => option.GetAttribute("value"));

        Assert.Equal(["b-mixed", "c-feature"], options);
    }

    [Fact]
    public void LayerWithoutAFeatureServer_OffersNoEditingForm()
    {
        var page = RenderPage(new RecordingMetadata(), Service("a-map", "MapServer"));

        page.WaitForAssertion(() => Assert.NotNull(page.Find("[data-editing-unavailable]")));
        Assert.Empty(page.FindAll("[data-editing-save]"));
    }

    [Fact]
    public void NavigatingToAnotherLayer_StartsFromAnUnchangedForm()
    {
        var metadata = new RecordingMetadata();
        var roads = new OperateServiceLayerProjection(2, "Roads", "LineString", "conn-1-layer-2", "roads");
        using var ctx = CreateContext(metadata,
            new OperateServiceDetail("cadastre", "cadastre", "FeatureServer", "Running", "honua-server", [Parcels, roads], [], []));
        var page = ctx.Render<OperateLayerMetadataPage>(p => p.Add(x => x.ResourceId, ResourceId));
        page.WaitForAssertion(() => Assert.NotNull(page.Find("[data-editing-save]")));
        page.Find("[data-editing-delete]").Change("false");
        page.Find("[data-editing-save]").Click();
        page.WaitForAssertion(() => Assert.NotNull(page.Find("[data-editing-result]")));

        page.Render(p => p.Add(x => x.ResourceId, roads.CanonicalResourceId));
        page.WaitForAssertion(() => Assert.Contains("Roads", page.Find("h1").TextContent, StringComparison.Ordinal));
        Assert.Empty(page.FindAll("[data-editing-result]"));
        page.Find("[data-editing-save]").Click();

        page.WaitForAssertion(() => Assert.Equal(2, metadata.Changes.Count));
        Assert.Equal(new ConsoleLayerEditingChange { ServiceName = "cadastre", LayerId = 2 }, metadata.Changes[1]);
    }

    private static OperateServiceDetail Service(string name, string protocols = "FeatureServer") =>
        new(name, name, protocols, "Running", "honua-server", [Parcels], [], []);

    private static IRenderedComponent<OperateLayerMetadataPage> RenderPage(
        RecordingMetadata metadata,
        params OperateServiceDetail[] services) =>
        CreateContext(metadata, services).Render<OperateLayerMetadataPage>(p => p.Add(x => x.ResourceId, ResourceId));

    private static BunitContext CreateContext(RecordingMetadata metadata, params OperateServiceDetail[] services)
    {
        var ctx = new BunitContext();
        ctx.AddConsoleNotifications();
        var data = services.Length == 0 ? FixedServicesDataSource.WithLayer(Parcels) : new FixedServicesDataSource(services);
        ctx.Services.AddSingleton<IOperateTransitionDataSource>(data);
        ctx.Services.AddSingleton<IConsoleLayerMetadataOperation>(metadata);
        return ctx;
    }

    private sealed class RecordingMetadata : IConsoleLayerMetadataOperation
    {
        public List<ConsoleLayerEditingChange> Changes { get; } = [];

        public ConsoleSetLayerMetadataResult Result { get; init; } =
            new() { Succeeded = true, State = "Updated", Detail = "Saved." };

        public Task<ConsoleSetLayerMetadataResult> SetEditingAsync(
            ConsoleLayerEditingChange change,
            CancellationToken cancellationToken = default)
        {
            Changes.Add(change);
            return Task.FromResult(Result);
        }
    }
}
