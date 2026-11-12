using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class OperateLayerLookupTests
{
    private static readonly OperateServiceLayerProjection ParcelsOnConnA =
        new(1, "Parcels", "Polygon", "conn-a-layer-1", "parcels");
    private static readonly OperateServiceLayerProjection ShelterOnConnA =
        new(2, "Shelters", "Point", "conn-a-layer-2", "shelters");
    private static readonly OperateServiceLayerProjection RoadsOnConnB =
        new(1, "Roads", "LineString", "conn-b-layer-1", "roads");

    [Theory]
    [InlineData("conn-a-layer-2")]
    [InlineData("CONN-A-LAYER-2")]
    [InlineData("2")]
    public void FindLayer_AcceptsCanonicalOrAdminLayerId(string reference)
    {
        var view = ViewOf(Service("cadastre", ParcelsOnConnA, ShelterOnConnA), Service("transport", RoadsOnConnB));

        Assert.Equal(ShelterOnConnA, view.FindLayer(reference));
    }

    [Theory]
    [InlineData("")]
    [InlineData("99")]
    [InlineData("-2")]
    [InlineData("conn-z-layer-2")]
    public void FindLayer_ReturnsNullForUnknownReference(string reference)
    {
        var view = ViewOf(Service("cadastre", ParcelsOnConnA, ShelterOnConnA));

        Assert.Null(view.FindLayer(reference));
    }

    [Fact]
    public void FindExposures_ListsEveryServiceExposingTheLayerByServiceName()
    {
        var view = ViewOf(Service("zoning", ShelterOnConnA), Service("emergency", ShelterOnConnA));

        var services = view.FindExposures("2").Select(exposure => exposure.Service.Name);

        Assert.Equal(["emergency", "zoning"], services);
    }

    [Fact]
    public void AdminLayerIdSharedByTwoConnections_ResolvesToNoLayerAndReportsBoth()
    {
        var view = ViewOf(Service("cadastre", ParcelsOnConnA), Service("transport", RoadsOnConnB));

        Assert.Empty(view.FindExposures("1"));
        Assert.Equal(["conn-a-layer-1", "conn-b-layer-1"], view.MatchingCanonicalResourceIds("1"));
    }

    [Fact]
    public void DetailPage_OpenedByPublishedAdminLayerId_RendersTheLayerWithCanonicalLinks()
    {
        using var ctx = RenderContext(Service("cadastre", ParcelsOnConnA));

        var page = ctx.Render<OperateLayerDetailPage>(p => p.Add(c => c.ResourceId, "1"));

        page.WaitForAssertion(() => Assert.Equal("Parcels", page.Find("h1").TextContent));
        Assert.Equal("/operate/layers/conn-a-layer-1/style", page.Find("[data-layer-style-link]").GetAttribute("href"));
    }

    [Fact]
    public void DetailPage_OpenedByAmbiguousAdminLayerId_LinksEachCandidate()
    {
        using var ctx = RenderContext(Service("cadastre", ParcelsOnConnA), Service("transport", RoadsOnConnB));

        var page = ctx.Render<OperateLayerDetailPage>(p => p.Add(c => c.ResourceId, "1"));

        page.WaitForAssertion(() => Assert.NotNull(page.Find("[data-layer-ambiguous]")));
        var links = page.FindAll("[data-layer-ambiguous] a").Select(link => link.GetAttribute("href"));
        Assert.Equal(["/operate/layers/conn-a-layer-1", "/operate/layers/conn-b-layer-1"], links);
    }

    private static BunitContext RenderContext(params OperateServiceDetail[] services)
    {
        var ctx = new BunitContext();
        ctx.AddConsoleNotifications();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IOperateTransitionDataSource>(new FixedServicesDataSource(services));
        ctx.Services.AddSingleton<IConsoleLayerFieldsOperation>(new UnsupportedConsoleLayerFieldsOperation());
        return ctx;
    }

    [Fact]
    public void GeometryByCanonicalResourceId_MatchesFindLayerForEveryServedResource()
    {
        var parcelsAsLines = ParcelsOnConnA with { Geometry = "LineString" };
        var view = ViewOf(Service("zoning", parcelsAsLines), Service("cadastre", ParcelsOnConnA, ShelterOnConnA));

        var geometry = view.GeometryByCanonicalResourceId();

        Assert.Equal(view.FindLayer("conn-a-layer-1")!.Geometry, geometry["conn-a-layer-1"]);
        Assert.Equal("Polygon", geometry["CONN-A-LAYER-1"]);
        Assert.Equal("Point", geometry["conn-a-layer-2"]);
        Assert.False(geometry.ContainsKey("conn-b-layer-1"));
    }

    private static OperateServicesView ViewOf(params OperateServiceDetail[] services) => new(services, []);

    private static OperateServiceDetail Service(string name, params OperateServiceLayerProjection[] layers) =>
        new(name, name, "FeatureServer", "Running", "honua-server", layers, [], []);
}
