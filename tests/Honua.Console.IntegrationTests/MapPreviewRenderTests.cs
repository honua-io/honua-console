using Bunit;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.Models;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free render coverage for the shared <see cref="MapPreview"/> component. Asserts the
/// design-handoff landmarks (map-preview.jsx): the no-backend SVG schematic placeholder, scale
/// control, zoom chrome, basemap chips, label/popup/highlight toggles, the optional class-break
/// legend, the service-mode layer-list overlay, and the as-of/now temporal framing. The component
/// must render fully without any map backend or JS runtime bound.
/// </summary>
public sealed class MapPreviewRenderTests
{
    [Fact]
    public void MapPreview_WithoutBackend_RendersSchematicScaleAndZoomChrome()
    {
        using var ctx = new Bunit.TestContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = ctx.RenderComponent<MapPreview>(parameters => parameters
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

        // Zoom chrome has in / out / recenter controls.
        Assert.Equal(3, cut.FindAll(".map-preview-zoom button").Count);
    }

    [Fact]
    public void MapPreview_LayerMode_TogglesLabelsHighlightAndPopup()
    {
        using var ctx = new Bunit.TestContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;

        var popup = new MapPreviewPopup(
            "Parcel 04-021-204 · 2,008 m²",
            [
                new MapPreviewPopupField("Use", "Single-family"),
                new MapPreviewPopupField("Owner", "— redacted —", Muted: true),
            ],
            "GetFeatureInfo preview · 4 of 23 fields exposed");

        var withChrome = ctx.RenderComponent<MapPreview>(parameters => parameters
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
        var bare = ctx.RenderComponent<MapPreview>(parameters => parameters
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
        using var ctx = new Bunit.TestContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = ctx.RenderComponent<MapPreview>(parameters => parameters
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
        using var ctx = new Bunit.TestContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;

        MapPreviewLayer? toggled = null;
        var cut = ctx.RenderComponent<MapPreview>(parameters => parameters
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
        using var ctx = new Bunit.TestContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;

        MapPreviewBasemap? selected = null;
        var cut = ctx.RenderComponent<MapPreview>(parameters => parameters
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
    }

    [Fact]
    public void MapPreview_TimeFrame_RendersAsOfAndNowBadges()
    {
        using var ctx = new Bunit.TestContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;

        var asOf = ctx.RenderComponent<MapPreview>(parameters => parameters
            .Add(p => p.TimeFrame, MapPreviewTimeFrame.AsOf)
            .Add(p => p.TimeFrameLabel, "2026-05-14 12:00 UTC · 10d ago"));
        Assert.Contains("map-preview-timeframe-asof", asOf.Markup, StringComparison.Ordinal);
        Assert.Contains("as-of", asOf.Markup, StringComparison.Ordinal);
        Assert.Contains("10d ago", asOf.Markup, StringComparison.Ordinal);

        var now = ctx.RenderComponent<MapPreview>(parameters => parameters
            .Add(p => p.TimeFrame, MapPreviewTimeFrame.Now)
            .Add(p => p.TimeFrameLabel, "current"));
        Assert.Contains("map-preview-timeframe-now", now.Markup, StringComparison.Ordinal);
        Assert.Contains(">now<", now.Markup, StringComparison.Ordinal);
    }
}
