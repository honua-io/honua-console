using System.Text.Json;
using Honua.Console.Shell.Models;

namespace Honua.Console.Native.Core.Tests;

/// <summary>
/// Host-independent coverage for the report-builder authoring model: the pure pre-publish gate that backs
/// the issue's "Vega-Lite is the chart spec for all report charts" + "version pinning/responsive preview
/// before publish" acceptance criteria, and the deterministic report-document serializer used as the
/// publish payload. No server or DOM is involved.
/// </summary>
public sealed class StudioReportBuilderModelsTests
{
    [Fact]
    public void DefaultBarChart_DeclaresVegaLiteSchema()
    {
        var spec = StudioReportChartSpec.DefaultBarChart("district", "incident_count");

        Assert.True(StudioReportChartSpec.DeclaresVegaLiteSchema(spec));
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
        Assert.False(StudioReportChartSpec.DeclaresVegaLiteSchema(spec));
    }

    [Fact]
    public void Evaluate_OnFullyAuthoredReport_AllowsPublish()
    {
        var state = AuthoredReport();

        var readiness = StudioReportPublishEvaluator.Evaluate(state);

        Assert.True(readiness.CanPublish);
        Assert.Empty(readiness.UnmetRequirements);
    }

    [Fact]
    public void Evaluate_WhenTitleMissingAndNoPanels_BlocksPublishWithRequirements()
    {
        var state = new StudioReportEditorState();

        var readiness = StudioReportPublishEvaluator.Evaluate(state);

        Assert.False(readiness.CanPublish);
        Assert.Contains(readiness.UnmetRequirements, r => r.Contains("title", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(readiness.UnmetRequirements, r => r.Contains("at least one panel", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Evaluate_WhenChartPanelLacksVegaLiteSpec_BlocksPublish()
    {
        var state = AuthoredReport();
        // Replace the chart spec with a non-Vega-Lite spec — the AC requires a Vega-Lite schema.
        state.Panels[0].VegaLiteSpec = "{\"mark\":\"bar\"}";

        var readiness = StudioReportPublishEvaluator.Evaluate(state);

        Assert.False(readiness.CanPublish);
        Assert.Contains(readiness.UnmetRequirements, r => r.Contains("Vega-Lite", StringComparison.Ordinal));
    }

    [Fact]
    public void Evaluate_WhenPanelBoundToUndeclaredAlias_BlocksPublish()
    {
        var state = AuthoredReport();
        state.Panels[0].BindingAlias = "unknown-alias";

        var readiness = StudioReportPublishEvaluator.Evaluate(state);

        Assert.False(readiness.CanPublish);
        Assert.Contains(readiness.UnmetRequirements, r => r.Contains("declared data binding", StringComparison.Ordinal));
    }

    [Fact]
    public void Evaluate_AllowsNonChartPanelsWithoutVegaLiteSpec()
    {
        var state = AuthoredReport();
        state.Panels.Add(new StudioReportPanelEditor
        {
            Title = "Incident map",
            Kind = StudioReportPanelKinds.Map,
            BindingAlias = "incidents"
        });

        var readiness = StudioReportPublishEvaluator.Evaluate(state);

        Assert.True(readiness.CanPublish);
    }

    [Fact]
    public void Serialize_ProducesDeterministicReportDocumentWithEmbeddedChartSpec()
    {
        var state = AuthoredReport();

        var first = StudioReportDocument.Serialize(state);
        var second = StudioReportDocument.Serialize(state);

        // Deterministic: the same editor state hashes/serializes identically (the server hashes the payload).
        Assert.Equal(first, second);

        using var document = JsonDocument.Parse(first);
        var root = document.RootElement;
        Assert.Equal(StudioReportDocument.Format, root.GetProperty("format").GetString());
        Assert.Equal("Monthly infrastructure report", root.GetProperty("title").GetString());

        // The Vega-Lite chart spec round-trips into the document as a nested JSON object, not a string.
        var panel = root.GetProperty("panels")[0];
        Assert.Equal(JsonValueKind.Object, panel.GetProperty("chartSpec").ValueKind);
        Assert.Contains(
            "vega-lite",
            panel.GetProperty("chartSpec").GetProperty("$schema").GetString(),
            StringComparison.OrdinalIgnoreCase);

        // The version pin is preserved so a published report reads a known content version.
        var binding = root.GetProperty("bindings")[0];
        Assert.Equal("v3", binding.GetProperty("versionPin").GetString());
    }

    [Fact]
    public void Serialize_PreservesInvalidChartSpecAsRawStringForServerRejection()
    {
        var state = AuthoredReport();
        state.Panels[0].VegaLiteSpec = "not json";

        var payload = StudioReportDocument.Serialize(state);

        using var document = JsonDocument.Parse(payload);
        var chartSpec = document.RootElement.GetProperty("panels")[0].GetProperty("chartSpec");
        Assert.Equal(JsonValueKind.String, chartSpec.ValueKind);
        Assert.Equal("not json", chartSpec.GetString());
    }

    private static StudioReportEditorState AuthoredReport()
    {
        var state = new StudioReportEditorState
        {
            Title = "Monthly infrastructure report",
            RouteSlug = "monthly-infrastructure",
            Narrative = "Context for the month.",
            Visibility = StudioReportVisibilities.Organization,
            Embeddable = true
        };
        state.Bindings.Add(new StudioReportBindingEditor { Alias = "incidents", ContentRef = "content:incidents", VersionPin = "v3" });
        state.Panels.Add(new StudioReportPanelEditor
        {
            Title = "Incidents by district",
            Kind = StudioReportPanelKinds.Chart,
            BindingAlias = "incidents",
            VegaLiteSpec = StudioReportChartSpec.DefaultBarChart("district", "incident_count")
        });
        return state;
    }
}
