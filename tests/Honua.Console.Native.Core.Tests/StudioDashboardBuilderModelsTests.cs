using Honua.Console.Shell.Models;

namespace Honua.Console.Native.Core.Tests;

/// <summary>
/// Host-independent coverage for the dashboard-builder authoring model: the pure pre-publish gate and the
/// Vega-Lite schema check that backs the issue's "Vega-Lite is the chart spec for all dashboard charts"
/// acceptance criterion. No server or DOM is involved.
/// </summary>
public sealed class StudioDashboardBuilderModelsTests
{
    [Fact]
    public void DefaultBarChart_DeclaresVegaLiteSchema()
    {
        var spec = StudioDashboardChartSpec.DefaultBarChart("district", "request_count");

        Assert.True(StudioDashboardChartSpec.DeclaresVegaLiteSchema(spec));
        Assert.Contains("vega-lite", spec, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"mark\":\"bar\"}")]
    [InlineData("{\"$schema\":\"https://vega.github.io/schema/vega/v5.json\"}")]
    public void DeclaresVegaLiteSchema_RejectsSpecsWithoutVegaLiteSchema(string? spec)
    {
        Assert.False(StudioDashboardChartSpec.DeclaresVegaLiteSchema(spec));
    }

    [Fact]
    public void Evaluate_ReadyDashboard_CanPublish()
    {
        var state = ReadyDashboard();

        var readiness = StudioDashboardPublishEvaluator.Evaluate(state);

        Assert.True(readiness.CanPublish);
        Assert.Empty(readiness.UnmetRequirements);
    }

    [Fact]
    public void Evaluate_EmptyDashboard_GatesPublish()
    {
        var readiness = StudioDashboardPublishEvaluator.Evaluate(new StudioDashboardEditorState());

        Assert.False(readiness.CanPublish);
        Assert.Contains(readiness.UnmetRequirements, r => r.Contains("title", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(readiness.UnmetRequirements, r => r.Contains("panel", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Evaluate_ChartPanelWithoutVegaLiteSpec_GatesPublish()
    {
        var state = ReadyDashboard();
        state.Panels[0].VegaLiteSpec = "{\"mark\":\"bar\"}"; // missing $schema

        var readiness = StudioDashboardPublishEvaluator.Evaluate(state);

        Assert.False(readiness.CanPublish);
        Assert.Contains(readiness.UnmetRequirements, r => r.Contains("Vega-Lite", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Evaluate_PanelBoundToUnknownAlias_GatesPublish()
    {
        var state = ReadyDashboard();
        state.Panels[0].BindingAlias = "does-not-exist";

        var readiness = StudioDashboardPublishEvaluator.Evaluate(state);

        Assert.False(readiness.CanPublish);
        Assert.Contains(readiness.UnmetRequirements, r => r.Contains("data binding", StringComparison.OrdinalIgnoreCase));
    }

    private static StudioDashboardEditorState ReadyDashboard()
    {
        var state = new StudioDashboardEditorState
        {
            DashboardId = "dashboard-1",
            Title = "Operations dashboard"
        };
        state.Bindings.Add(new StudioDashboardBindingEditor
        {
            Alias = "requests",
            ContentRef = "content:service-requests",
            VersionPin = "v5"
        });
        state.Panels.Add(new StudioDashboardPanelEditor
        {
            Title = "Requests by district",
            Kind = StudioDashboardPanelKinds.Chart,
            BindingAlias = "requests",
            VegaLiteSpec = StudioDashboardChartSpec.DefaultBarChart("district", "request_count")
        });
        return state;
    }
}
