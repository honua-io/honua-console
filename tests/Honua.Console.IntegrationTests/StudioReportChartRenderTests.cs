using Bunit;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.Models;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free render coverage for the Studio REPORT family's final visible output: a report chart
/// panel renders the shared <see cref="ChartPreview"/> over the panel's REAL generated Vega-Lite spec
/// (the report model's <see cref="StudioReportPanelEditor.VegaLiteSpec"/>, mirroring the dashboard's
/// chart panel), and a report map panel renders the shared <see cref="MapPreview"/>. The preview must
/// render fully without any Vega/JS runtime or map backend bound, degrading to the honest schematic
/// placeholder rather than fabricating data (Charter §11). Rendering the full builder page requires the
/// publication data source + a load flow, so this exercises the smallest renderable unit — the panel's
/// spec driving ChartPreview — which is exactly what the report page emits per chart panel.
/// </summary>
public sealed class StudioReportChartRenderTests
{
    [Fact]
    public void ReportChartPanel_RendersChartPreviewOverPanelSpec()
    {
        using var ctx = new Bunit.BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;

        // A real report chart panel as the builder authors it (AddPanel seeds DefaultBarChart()).
        var panel = new StudioReportPanelEditor
        {
            Title = "Incidents by category",
            Kind = StudioReportPanelKinds.Chart,
            VegaLiteSpec = StudioReportChartSpec.DefaultBarChart("category", "value"),
        };

        Assert.True(panel.IsChart);
        // The panel carries a real Vega-Lite spec (the spec the report page hands ChartPreview).
        Assert.True(StudioReportChartSpec.DeclaresVegaLiteSchema(panel.VegaLiteSpec));

        var cut = ctx.Render<ChartPreview>(parameters => parameters
            .Add(p => p.Spec, panel.VegaLiteSpec)
            .Add(p => p.Height, 200)
            .Add(p => p.AriaLabel, $"{panel.Title} chart"));

        // The report panel emits a ChartPreview figure (class chart-preview).
        Assert.Contains("chart-preview", cut.Markup, StringComparison.Ordinal);
        // Without a Vega/JS runtime the honest schematic placeholder is shown (no fabricated data).
        Assert.Contains("chart-preview-schematic", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("data-chart-bound=\"false\"", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("<rect", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportChartPanel_WithoutSpec_RendersHonestSchematicPlaceholder()
    {
        using var ctx = new Bunit.BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;

        // A chart panel with no spec yet (unbound) must still render an honest placeholder, never blank.
        var panel = new StudioReportPanelEditor { Kind = StudioReportPanelKinds.Chart };

        var cut = ctx.Render<ChartPreview>(parameters => parameters
            .Add(p => p.Spec, panel.VegaLiteSpec)
            .Add(p => p.Height, 200));

        Assert.Contains("chart-preview-schematic", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("data-chart-bound=\"false\"", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportMapPanel_RendersMapPreviewSchematicLayerMode()
    {
        using var ctx = new Bunit.BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;

        var panel = new StudioReportPanelEditor { Title = "Coverage", Kind = StudioReportPanelKinds.Map };
        Assert.False(panel.IsChart);

        // The report map panel emits a MapPreview in Layer mode with popups suppressed (mirrors dashboard).
        var cut = ctx.Render<MapPreview>(parameters => parameters
            .Add(p => p.Mode, MapPreviewMode.Layer)
            .Add(p => p.Height, 200)
            .Add(p => p.ShowPopup, false)
            .Add(p => p.AriaLabel, $"{panel.Title} preview"));

        // No map backend bound: honest schematic, never a fabricated map.
        Assert.Contains("map-preview-schematic", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("data-map-bound=\"false\"", cut.Markup, StringComparison.Ordinal);
    }
}
