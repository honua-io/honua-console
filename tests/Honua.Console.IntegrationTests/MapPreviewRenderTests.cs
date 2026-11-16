using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.Models;

namespace Honua.Console.IntegrationTests;

public sealed class MapPreviewRenderTests
{
    [Fact]
    public void MapPreview_WithNoScaleSupplied_OmitsTheStripRatherThanAssertingOne()
    {
        using var ctx = new Bunit.BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = ctx.Render<MapPreview>();

        // ScaleText / Crs / Zoom used to default to "1:8,000" / "EPSG:4326" / 14, so every preview
        // asserted a scale, a CRS and a zoom that nothing had read — including on layer pages where
        // the CRS chip read as that layer's declared CRS. With none supplied, no strip renders.
        Assert.Empty(cut.FindAll("[data-map-scale]"));

        // The schematic itself is unaffected: it still draws, and still reports it is not bound.
        Assert.Contains("map-preview-schematic", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("data-map-bound=\"false\"", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void MapPreview_WithOnlyCrsSupplied_RendersThatChipAlone()
    {
        using var ctx = new Bunit.BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = ctx.Render<MapPreview>(parameters => parameters.Add(p => p.Crs, "EPSG:3857"));

        var scale = cut.Find("[data-map-scale]");
        Assert.Contains("EPSG:3857", scale.TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("z ", scale.TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("1:", scale.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void MapPreview_WithoutBackend_RendersSchematicScaleAndZoomChrome()
    {
        using var ctx = new Bunit.BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = ctx.Render<MapPreview>(parameters => parameters
            .Add(p => p.ScaleText, "1:8,000")
            .Add(p => p.Crs, "EPSG:4326")
            .Add(p => p.Zoom, 14));

        // No-binding placeholder is present and the live map is not bound.
        Assert.Contains("map-preview-schematic", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("data-map-bound=\"false\"", cut.Markup, StringComparison.Ordinal);

        // Scale control reads the supplied scale + CRS + zoom.
        var scale = cut.Find("[data-map-scale]");
        Assert.Contains("1:8,000", scale.TextContent, StringComparison.Ordinal);
        Assert.Contains("EPSG:4326", scale.TextContent, StringComparison.Ordinal);
        Assert.Contains("z 14", scale.TextContent, StringComparison.Ordinal);

        // Zoom chrome has in / out / recenter controls. With no live map bound there is no camera to drive,
        // so they are honestly disabled (never clickable no-ops over the static schematic) with a title that
        // explains why. They wire to the live MapLibre camera only once a style is bound. (Charter §11.)
        var zoomButtons = cut.FindAll(".map-preview-zoom button");
        Assert.Equal(3, zoomButtons.Count);
        Assert.All(zoomButtons, b => Assert.True(b.HasAttribute("disabled")));
        Assert.All(zoomButtons, b => Assert.False(string.IsNullOrWhiteSpace(b.GetAttribute("title"))));
    }

    [Fact]
    public void MapPreview_LayerMode_TogglesLabelsHighlightAndPopup()
    {
        using var ctx = new Bunit.BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;

        var popup = new MapPreviewPopup(
            "Parcel 04-021-204 · 2,008 m²",
            [
                new MapPreviewPopupField("Use", "Single-family"),
                new MapPreviewPopupField("Owner", "— redacted —", Muted: true),
            ],
            "GetFeatureInfo preview · 4 of 23 fields exposed");

        var withChrome = ctx.Render<MapPreview>(parameters => parameters
            .Add(p => p.Mode, MapPreviewMode.Layer)
            .Add(p => p.ShowLabels, true)
            .Add(p => p.ShowHighlight, true)
            .Add(p => p.ShowPopup, true)
            .Add(p => p.Popup, popup));

        Assert.Contains("map-preview-labels", withChrome.Markup, StringComparison.Ordinal);
        Assert.Contains("map-preview-highlight", withChrome.Markup, StringComparison.Ordinal);
        Assert.Contains("map-preview-popup", withChrome.Markup, StringComparison.Ordinal);
        Assert.Contains("Parcel 04-021-204", withChrome.Markup, StringComparison.Ordinal);
        Assert.Contains("— redacted —", withChrome.Markup, StringComparison.Ordinal);

        // Disabling the toggles removes each landmark; popup hidden even though content is supplied.
        var bare = ctx.Render<MapPreview>(parameters => parameters
            .Add(p => p.Mode, MapPreviewMode.Layer)
            .Add(p => p.ShowLabels, false)
            .Add(p => p.ShowHighlight, false)
            .Add(p => p.ShowPopup, false)
            .Add(p => p.Popup, popup));

        Assert.DoesNotContain("map-preview-labels", bare.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("map-preview-highlight", bare.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("map-preview-popup", bare.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void MapPreview_WithLegend_RendersClassBreakSwatches()
    {
        using var ctx = new Bunit.BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = ctx.Render<MapPreview>(parameters => parameters
            .Add(p => p.LegendTitle, "use_code")
            .Add(p => p.Legend,
            [
                new MapPreviewLegendEntry("R-1 Residential", "#ead78a", "812 parcels"),
                new MapPreviewLegendEntry("C-2 Commercial", "#612d0a"),
            ]));

        Assert.Contains("map-preview-legend", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("use_code", cut.Markup, StringComparison.Ordinal);
        Assert.Equal(2, cut.FindAll(".map-preview-legend-row").Count);
        Assert.Contains("812 parcels", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void MapPreview_ServiceMode_RendersLayerListOverlayAndRaisesToggle()
    {
        using var ctx = new Bunit.BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;

        MapPreviewLayer? toggled = null;
        var cut = ctx.Render<MapPreview>(parameters => parameters
            .Add(p => p.Mode, MapPreviewMode.Service)
            .Add(p => p.Layers,
            [
                new MapPreviewLayer(0, "Parcels", "#d9a23a", Visible: true),
                new MapPreviewLayer(4, "Fire perimeters", "#aa3a2b", Visible: false),
            ])
            .Add(p => p.OnLayerToggled, layer => toggled = layer));

        Assert.Contains("map-preview-layer-list", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Layers · 2", cut.Markup, StringComparison.Ordinal);
        Assert.Equal(2, cut.FindAll(".map-preview-layer-row").Count);

        cut.FindAll(".map-preview-layer-row input")[0].Click();
        Assert.NotNull(toggled);
        Assert.Equal(0, toggled!.Id);
    }

    [Fact]
    public void MapPreview_Basemaps_SelectActivatesChipAndRaisesCallback()
    {
        using var ctx = new Bunit.BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;

        MapPreviewBasemap? selected = null;
        var cut = ctx.Render<MapPreview>(parameters => parameters
            .Add(p => p.Basemaps,
            [
                new MapPreviewBasemap("positron", "Positron"),
                new MapPreviewBasemap("satellite", "Satellite"),
            ])
            .Add(p => p.OnBasemapChanged, basemap => selected = basemap));

        // First chip is active by default.
        Assert.Contains("map-preview-basemap-chip-active", cut.Markup, StringComparison.Ordinal);
        var chips = cut.FindAll(".map-preview-basemap-chip");
        Assert.Equal(2, chips.Count);

        chips[1].Click();
        Assert.NotNull(selected);
        Assert.Equal("satellite", selected!.Id);
        // The newly selected chip carries the active state.
        Assert.True(cut.FindAll(".map-preview-basemap-chip")[1].ClassList.Contains("map-preview-basemap-chip-active"));
        // Toggle state is an explicit ARIA token on every chip; a bare bool renders aria-pressed="" or drops it.
        Assert.Equal(new string?[] { "false", "true" }, cut.FindAll(".map-preview-basemap-chip").Select(c => c.GetAttribute("aria-pressed")).ToArray());
    }

    [Fact]
    public void MapPreview_TimeFrame_RendersAsOfAndNowBadges()
    {
        using var ctx = new Bunit.BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;

        var asOf = ctx.Render<MapPreview>(parameters => parameters
            .Add(p => p.TimeFrame, MapPreviewTimeFrame.AsOf)
            .Add(p => p.TimeFrameLabel, "2026-05-14 12:00 UTC · 10d ago"));
        Assert.Contains("map-preview-timeframe-asof", asOf.Markup, StringComparison.Ordinal);
        Assert.Contains("as-of", asOf.Markup, StringComparison.Ordinal);
        Assert.Contains("10d ago", asOf.Markup, StringComparison.Ordinal);

        var now = ctx.Render<MapPreview>(parameters => parameters
            .Add(p => p.TimeFrame, MapPreviewTimeFrame.Now)
            .Add(p => p.TimeFrameLabel, "current"));
        Assert.Contains("map-preview-timeframe-now", now.Markup, StringComparison.Ordinal);
        Assert.Contains(">now<", now.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MapPreview_DisposedWhileImporting_DoesNotInitializeMap()
    {
        using var ctx = new BunitContext();
        var runtime = new DeferredMapRuntime();
        ctx.Services.AddSingleton<IJSRuntime>(runtime);
        var cut = ctx.Render<MapPreview>(parameters => parameters.Add(p => p.StyleUrl, "/style.json"));
        cut.Render(parameters => parameters.Add(p => p.AriaLabel, "Updated caption"));
        Assert.Equal(1, runtime.Imports);
        Assert.True(runtime.ImportStarted);
        Assert.False(runtime.Import.Task.IsCompleted);

        await ctx.DisposeComponentsAsync();
        var module = new MapModule();
        await ctx.Renderer.Dispatcher.InvokeAsync(() => runtime.Import.SetResult(module));
        await module.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(0, module.Initializations);
        Assert.Equal(1, module.Disposals);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public void MapPreview_ClearingStyle_RemovesBoundMapAndDisablesZoom(string? missingStyle)
    {
        using var ctx = new BunitContext();
        var module = ctx.JSInterop.SetupModule("./_content/Honua.Console.Shell/map-preview.js");
        module.Setup<bool>("init", _ => true).SetResult(true);
        module.SetupVoid("dispose", _ => true).SetVoidResult();
        var cut = ctx.Render<MapPreview>(parameters => parameters.Add(p => p.StyleUrl, "/style.json"));
        cut.WaitForAssertion(() => Assert.Equal("true", cut.Find("figure").GetAttribute("data-map-bound")));

        cut.Render(parameters => parameters.Add(p => p.StyleUrl, missingStyle));

        cut.WaitForAssertion(() => Assert.Equal("false", cut.Find("figure").GetAttribute("data-map-bound")));
        Assert.Single(module.Invocations["dispose"]);
        Assert.All(cut.FindAll(".map-preview-zoom button"), button => Assert.True(button.HasAttribute("disabled")));

        cut.Render(parameters => parameters.Add(p => p.StyleUrl, "/style.json"));
        cut.WaitForAssertion(() => Assert.Equal("true", cut.Find("figure").GetAttribute("data-map-bound")));
        Assert.Equal(2, module.Invocations["init"].Count);
    }

    [Fact]
    public async Task MapPreview_ClearingStyleWhileImporting_DoesNotInitializeMap()
    {
        using var ctx = new BunitContext();
        var runtime = new DeferredMapRuntime();
        ctx.Services.AddSingleton<IJSRuntime>(runtime);
        var cut = ctx.Render<MapPreview>(parameters => parameters.Add(p => p.StyleUrl, "/style.json"));
        Assert.True(runtime.ImportStarted);
        cut.Render(parameters => parameters.Add(p => p.StyleUrl, (string?)null));

        var module = new MapModule();
        await ctx.Renderer.Dispatcher.InvokeAsync(() => runtime.Import.SetResult(module));

        Assert.Equal(0, module.Initializations);
        Assert.Equal("false", cut.Find("figure").GetAttribute("data-map-bound"));
    }

    [Fact]
    public async Task MapPreview_RerenderingWhileImporting_ImportsAndInitializesOnce()
    {
        using var ctx = new BunitContext();
        var runtime = new DeferredMapRuntime();
        ctx.Services.AddSingleton<IJSRuntime>(runtime);
        var cut = ctx.Render<MapPreview>(p => p.Add(x => x.StyleUrl, "/style.json"));
        cut.Render(p => p.Add(x => x.AriaLabel, "Updated caption"));
        cut.Render(p => p.Add(x => x.StyleUrl, "/replacement.json"));
        Assert.Equal(1, runtime.Imports);

        var module = new MapModule();
        await ctx.Renderer.Dispatcher.InvokeAsync(() => runtime.Import.SetResult(module));

        cut.WaitForAssertion(() => Assert.Equal("true", cut.Find("figure").GetAttribute("data-map-bound")));
        Assert.Equal(1, module.Initializations);
        Assert.Equal("/replacement.json", module.LastStyleUrl);
    }

    [Fact]
    public async Task MapPreview_RerenderingWhileInitializing_KeepsPendingMap()
    {
        using var ctx = new BunitContext();
        var module = ctx.JSInterop.SetupModule("./_content/Honua.Console.Shell/map-preview.js");
        var initialization = module.Setup<bool>("init", _ => true);
        module.SetupVoid("dispose", _ => true).SetVoidResult();
        var cut = ctx.Render<MapPreview>(p => p.Add(x => x.StyleUrl, "/style.json"));

        cut.Render(p => p.Add(x => x.AriaLabel, "Updated caption"));
        Assert.Single(module.Invocations["init"]);
        await ctx.Renderer.Dispatcher.InvokeAsync(() => initialization.SetResult(true));

        cut.WaitForAssertion(() => Assert.Equal("true", cut.Find("figure").GetAttribute("data-map-bound")));
    }

    [Fact]
    public async Task MapPreview_ClearingStyleWhileInitializing_DiscardsLateResult()
    {
        using var ctx = new BunitContext();
        var module = ctx.JSInterop.SetupModule("./_content/Honua.Console.Shell/map-preview.js");
        var initialization = module.Setup<bool>("init", _ => true);
        module.SetupVoid("dispose", _ => true).SetVoidResult();
        var cut = ctx.Render<MapPreview>(p => p.Add(x => x.StyleUrl, "/style.json"));

        cut.Render(p => p.Add(x => x.StyleUrl, (string?)null));
        Assert.Single(module.Invocations["dispose"]);
        await ctx.Renderer.Dispatcher.InvokeAsync(() => initialization.SetResult(true));

        Assert.Equal("false", cut.Find("figure").GetAttribute("data-map-bound"));
        Assert.All(cut.FindAll(".map-preview-zoom button"), button => Assert.True(button.HasAttribute("disabled")));
    }

    [Fact]
    public void MapPreview_CanceledImport_KeepsSchematicAndCanRetry()
    {
        using var ctx = new BunitContext();
        var runtime = new DeferredMapRuntime();
        runtime.Import.SetCanceled();
        ctx.Services.AddSingleton<IJSRuntime>(runtime);
        var cut = ctx.Render<MapPreview>(p => p.Add(x => x.StyleUrl, "/style.json"));
        Assert.Equal("false", cut.Find("figure").GetAttribute("data-map-bound"));

        runtime.Import = new();
        var module = new MapModule();
        runtime.Import.SetResult(module);
        cut.Render(p => p.Add(x => x.AriaLabel, "Retry unchanged style"));

        cut.WaitForAssertion(() => Assert.Equal("true", cut.Find("figure").GetAttribute("data-map-bound")));
        Assert.Equal(2, runtime.Imports);
        Assert.Equal(1, module.Initializations);
    }

    private sealed class DeferredMapRuntime : IJSRuntime
    {
        public TaskCompletionSource<IJSObjectReference> Import { get; set; } = new();
        public int Imports { get; private set; }
        public bool ImportStarted { get; private set; }

        public async ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            ImportStarted = true;
            Imports++;
            return (TValue)await Import.Task;
        }

        // This test runtime forwards to its own fake implementation, which must expose failures to the test.
#pragma warning disable BL0016
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            InvokeAsync<TValue>(identifier, args);
#pragma warning restore BL0016
    }

    private sealed class MapModule : IJSObjectReference
    {
        public int Initializations { get; private set; }
        public string? LastStyleUrl { get; private set; }
        public TaskCompletionSource Disposed { get; } = new();
        public int Disposals { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            if (identifier == "init")
            {
                Initializations++;
                LastStyleUrl = System.Text.Json.JsonSerializer.SerializeToElement(args![1]).GetProperty("styleUrl").GetString();
                return ValueTask.FromResult((TValue)(object)true);
            }

            return ValueTask.FromResult(default(TValue)!);
        }

        // This test runtime forwards to its own fake implementation, which must expose failures to the test.
#pragma warning disable BL0016
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            InvokeAsync<TValue>(identifier, args);
#pragma warning restore BL0016

        public ValueTask DisposeAsync()
        {
            Disposals++;
            Disposed.TrySetResult();
            return ValueTask.CompletedTask;
        }
    }
}
