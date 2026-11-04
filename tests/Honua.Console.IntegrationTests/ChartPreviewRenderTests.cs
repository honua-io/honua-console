using Bunit;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free render coverage for the shared <see cref="ChartPreview"/> component (the dashboard/
/// report/analysis "graphs" result, analog to <see cref="MapPreview"/>) and the
/// <see cref="StudioQueryResultChart"/> spec/binding helper that drives the query family's live result.
/// The component must render its no-backend SVG bar-chart schematic fully without any Vega runtime or
/// JS interop bound, and only ever charts a query that carries a real source binding (no fabricated
/// data — Charter §11).
/// </summary>
public sealed class ChartPreviewRenderTests
{
    [Fact]
    public void ChartPreview_WithoutSpec_RendersSchematicPlaceholderUnbound()
    {
        using var ctx = new Bunit.BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = ctx.Render<ChartPreview>(parameters => parameters
            .Add(p => p.Title, "Features by name"));

        // No-binding placeholder is present and no live chart is bound.
        Assert.Contains("chart-preview-schematic", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("data-chart-bound=\"false\"", cut.Markup, StringComparison.Ordinal);
        // The schematic draws bars so the empty state reads as a chart, not a blank box.
        Assert.Contains("<rect", cut.Markup, StringComparison.Ordinal);
        // Optional caption renders.
        Assert.Contains("Features by name", cut.Markup, StringComparison.Ordinal);
    }

}
