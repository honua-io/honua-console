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

    private static OperateServiceDetail Service(string name) =>
        new(name, name, "FeatureServer", "Running", "honua-server", [Parcels], [], []);

    private static IRenderedComponent<OperateLayerMetadataPage> RenderPage(
        RecordingMetadata metadata,
        params OperateServiceDetail[] services)
    {
        var ctx = new BunitContext();
        ctx.AddConsoleNotifications();
        var data = services.Length == 0 ? FixedServicesDataSource.WithLayer(Parcels) : new FixedServicesDataSource(services);
        ctx.Services.AddSingleton<IOperateTransitionDataSource>(data);
        ctx.Services.AddSingleton<IConsoleLayerMetadataOperation>(metadata);
        return ctx.Render<OperateLayerMetadataPage>(p => p.Add(x => x.ResourceId, ResourceId));
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
