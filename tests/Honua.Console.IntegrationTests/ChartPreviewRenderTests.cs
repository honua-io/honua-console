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
        using var ctx = new Bunit.TestContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = ctx.RenderComponent<ChartPreview>(parameters => parameters
            .Add(p => p.Title, "Features by name"));

        // No-binding placeholder is present and no live chart is bound.
        Assert.Contains("chart-preview-schematic", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("data-chart-bound=\"false\"", cut.Markup, StringComparison.Ordinal);
        // The schematic draws bars so the empty state reads as a chart, not a blank box.
        Assert.Contains("<rect", cut.Markup, StringComparison.Ordinal);
        // Optional caption renders.
        Assert.Contains("Features by name", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void StudioQueryResultChart_BoundQuery_ProducesRealFeaturesUrlAndMatchingSpec()
    {
        var query = new StudioQueryEditor { ServiceName = "e2e_src_fs", LayerId = 1 };
        query.OutFields.Add("id");
        query.OutFields.Add("name");

        Assert.True(StudioQueryResultChart.IsBound(query));

        // The features URL targets the console proxy for the bound service/layer — the REAL rows path.
        Assert.Equal("/map-proxy/features/e2e_src_fs/1", StudioQueryResultChart.FeaturesUrl(query));

        // The dimension skips the id/objectid key and groups by the first real attribute field.
        Assert.Equal("name", StudioQueryResultChart.DimensionField(query));

        // The spec is a valid Vega-Lite count-by-dimension bar chart whose x-encoding binds the real field,
        // so the live rows (id/name) plot correctly.
        var spec = StudioQueryResultChart.BuildSpec(query);
        Assert.NotNull(spec);
        using var doc = System.Text.Json.JsonDocument.Parse(spec!);
        var root = doc.RootElement;
        Assert.Contains("vega-lite", root.GetProperty("$schema").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("bar", root.GetProperty("mark").GetProperty("type").GetString());
        Assert.Equal("name", root.GetProperty("encoding").GetProperty("x").GetProperty("field").GetString());
        Assert.Equal("count", root.GetProperty("encoding").GetProperty("y").GetProperty("aggregate").GetString());
    }

    [Fact]
    public void StudioQueryResultChart_UnboundQuery_HasNoResultToChart()
    {
        var query = new StudioQueryEditor { ServiceName = string.Empty };

        Assert.False(StudioQueryResultChart.IsBound(query));
        Assert.Null(StudioQueryResultChart.FeaturesUrl(query));
        Assert.Null(StudioQueryResultChart.BuildSpec(query));
    }
}
