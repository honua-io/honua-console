using Bunit;
using Honua.Console.Shell.Components.Operate;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class LayerPreviewPaneRenderTests
{
    private static readonly LayerPreviewPane.PreviewSelection Selection =
        new("conn-1-layer-7", "Parcels", "featureserver", "FeatureServer", "/rest/services/cadastre/FeatureServer/7");

    private static readonly OperateServiceLayerProjection Parcels =
        new(7, "Parcels", "Polygon", "conn-1-layer-7", "parcels", [-158.3, 21.2, -157.6, 21.7]);

    [Fact]
    public void SelectedPublication_MountsALiveMapOnTheProxiedLayerStyle()
    {
        using var ctx = new BunitContext();
        var module = ctx.JSInterop.SetupModule("./_content/Honua.Console.Shell/map-preview.js");
        module.Mode = JSRuntimeMode.Loose;

        var pane = ctx.Render<LayerPreviewPane>(p => p
            .Add(x => x.Selection, Selection)
            .Add(x => x.Layer, Parcels)
            .Add(x => x.EnvironmentId, "env-1"));

        pane.WaitForAssertion(() => Assert.Single(module.Invocations["init"]));
        Assert.Contains("/map-proxy/env-1/styles/7.json", System.Text.Json.JsonSerializer.Serialize(module.Invocations["init"][0].Arguments[1]), StringComparison.Ordinal);
        Assert.Equal("Map preview of layer Parcels", pane.Find("figure.map-preview").GetAttribute("aria-label"));
    }

    [Fact]
    public void ConfiguredApiStyle_KeepsTheApiPrefixAndRequiresAnEnvironment()
    {
        var address = new ConfiguredApiPreviewAddress(new Uri("https://api.example/base/"));
        Assert.Equal("https://api.example/base/api/styles/7.json", address.LayerStyle("env-1", 7));
        Assert.Null(address.LayerStyle(null, 7));
        Assert.Null(address.LayerStyle(" ", 7));
        Assert.Null(new ConfiguredApiPreviewAddress(new Uri("https://api.example/base")).LayerStyle("env-1", 7));
        Assert.Null(new ConfiguredApiPreviewAddress(new Uri("https://user@api.example/base/")).LayerStyle("env-1", 7));
    }

    [Fact]
    public void SelectedPublication_UsesTheConfiguredApiStyleWhenTheHostPublishesOne()
    {
        using var ctx = new BunitContext();
        ctx.Services.AddSingleton<IConsolePreviewAddress>(new ConfiguredApiPreviewAddress(new Uri("https://api.example/base/")));
        var module = ctx.JSInterop.SetupModule("./_content/Honua.Console.Shell/map-preview.js");
        module.Mode = JSRuntimeMode.Loose;

        var pane = ctx.Render<LayerPreviewPane>(p => p
            .Add(x => x.Selection, Selection)
            .Add(x => x.Layer, Parcels)
            .Add(x => x.EnvironmentId, "env-1"));

        pane.WaitForAssertion(() => Assert.Single(module.Invocations["init"]));
        var arguments = System.Text.Json.JsonSerializer.Serialize(module.Invocations["init"][0].Arguments[1]);
        Assert.Contains("https://api.example/base/api/styles/7.json", arguments, StringComparison.Ordinal);
        Assert.DoesNotContain("/map-proxy/", arguments, StringComparison.Ordinal);
    }

    [Fact]
    public void SelectedPublication_WithoutAServedLayer_SaysSoInsteadOfDrawingPlaceholderTiles()
    {
        using var ctx = new BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;

        var pane = ctx.Render<LayerPreviewPane>(p => p.Add(x => x.Selection, Selection));

        Assert.NotNull(pane.Find("[data-preview-unrendered]"));
        Assert.Empty(pane.FindAll("figure.map-preview"));
    }
}
