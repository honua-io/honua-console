using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Unit coverage for the deterministic, Console-side <see cref="EsriContentImportParser"/>. Feeds small
/// Esri-shaped JSON documents and asserts the honua.map-package / dashboard / report mapping and the
/// clean / degrade / drop / manual fidelity classification (the design-handoff Fid convention). The parser
/// contacts no server and fabricates no data (Console Patterns Charter section 11).
/// </summary>
public sealed class EsriContentImportParserTests
{
    private readonly EsriContentImportParser _parser = new();

    [Fact]
    public void ParseWebMap_ClassifiesLayerFidelityAndMapsToMapPackage()
    {
        const string json = """
        {
          "title": "Public Works",
          "operationalLayers": [
            { "id": "parcels", "title": "Parcels", "layerType": "ArcGISFeatureLayer", "url": "https://services/Parcels/FeatureServer/0", "popupInfo": { "title": "{ID}" } },
            { "id": "hydrants", "title": "Hydrants", "layerType": "ArcGISFeatureLayer" },
            { "id": "landuse", "title": "Land use", "layerType": "ArcGISFeatureLayer", "url": "https://services/LandUse/0",
              "layerDefinition": { "drawingInfo": { "renderer": { "type": "uniqueValue" } } } },
            { "id": "heat", "title": "Heatmap 311", "layerType": "ArcGISFeatureLayer", "url": "https://services/Calls/0",
              "layerDefinition": { "drawingInfo": { "renderer": { "type": "heatmap" } } } }
          ],
          "baseMap": { "baseMapLayers": [ { "id": "imagery", "layerType": "ArcGISTiledMapServiceLayer", "url": "https://tiles" } ] },
          "bookmarks": [ { "name": "A" }, { "name": "B" } ],
          "widgets": [ { "type": "TimeSlider", "title": "Time slider" } ]
        }
        """;

        var outcome = _parser.ParseWebMap(json, "wm.json");

        Assert.True(outcome.Succeeded, outcome.Error);
        var result = outcome.Result!;
        Assert.Equal(EsriContentKind.WebMap, result.Kind);
        Assert.Equal("honua.map-package.v1", result.TargetSchema);

        // Parcels: resolvable URL -> clean, mapped to a fill layer.
        var parcels = result.Rows.Single(r => r.SourceName == "Parcels");
        Assert.Equal(ImportFidelity.Clean, parcels.Fidelity);
        Assert.Equal("parcels/fill", parcels.TargetName);

        // Hydrants: no resolvable source -> manual (needs a data-resource binding).
        var hydrants = result.Rows.Single(r => r.SourceName == "Hydrants");
        Assert.Equal(ImportFidelity.Manual, hydrants.Fidelity);
        Assert.Contains("resource", hydrants.Note, StringComparison.OrdinalIgnoreCase);

        // Land use: uniqueValue + Arcade label -> degrade, with the named reason.
        var landuse = result.Rows.Single(r => r.SourceName == "Land use");
        Assert.Equal(ImportFidelity.Degrade, landuse.Fidelity);
        Assert.Contains("static label", landuse.Note, StringComparison.OrdinalIgnoreCase);

        // Heatmap renderer -> drop, not representable in map-package.v1.
        var heat = result.Rows.Single(r => r.SourceName == "Heatmap 311");
        Assert.Equal(ImportFidelity.Drop, heat.Fidelity);
        Assert.False(heat.Included);

        // Widget -> drop row.
        Assert.Contains(result.Rows, r => r.SourceType == "Widget" && r.Fidelity == ImportFidelity.Drop);

        // Aggregate counts.
        Assert.Equal(1, result.ManualCount);
        Assert.Equal(1, result.DegradeCount);
        Assert.Equal(2, result.DropCount); // heatmap + widget
        Assert.True(result.HasUnboundLayers);
        Assert.Equal("Hydrants", result.FirstUnbound!.SourceName);

        // Carry-over chips reflect the basemap + bookmarks.
        Assert.Contains(result.CarryOver, c => c.Label == "basemap" && c.Carried);
        Assert.Contains(result.CarryOver, c => c.Label.Contains("bookmark", StringComparison.OrdinalIgnoreCase) && c.Carried);
    }

    [Fact]
    public void ParseWebMap_InvalidJson_ReturnsFailure()
    {
        var outcome = _parser.ParseWebMap("{ not json", "bad.json");

        Assert.False(outcome.Succeeded);
        Assert.NotNull(outcome.Error);
    }

    [Fact]
    public void ParseWebMap_NoOperationalLayers_ReturnsFailure()
    {
        var outcome = _parser.ParseWebMap("""{ "operationalLayers": [] }""");

        Assert.False(outcome.Succeeded);
        Assert.Contains("operationalLayers", outcome.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParseDashboard_ClassifiesElementFidelity()
    {
        const string json = """
        {
          "title": "Q3 Ops",
          "headerPanel": { "type": "header", "name": "Header" },
          "widgets": [
            { "type": "indicator", "name": "Total", "datasetId": "parcels" },
            { "type": "gauge", "name": "Capacity", "datasetId": "obs" },
            { "type": "richText", "name": "Embed" }
          ],
          "selectors": [ { "type": "categorySelector", "name": "Category" } ]
        }
        """;

        var outcome = _parser.ParseDashboard(json, "dash.json");

        Assert.True(outcome.Succeeded, outcome.Error);
        var result = outcome.Result!;
        Assert.Equal(EsriContentKind.Dashboard, result.Kind);
        Assert.Equal(ImportFidelity.Clean, result.Rows.Single(r => r.SourceName == "Total").Fidelity);
        Assert.Equal(ImportFidelity.Degrade, result.Rows.Single(r => r.SourceName == "Capacity").Fidelity);
        Assert.Equal(ImportFidelity.Drop, result.Rows.Single(r => r.SourceName == "Embed").Fidelity);
        Assert.Equal(ImportFidelity.Clean, result.Rows.Single(r => r.SourceName == "Category").Fidelity);
    }

    [Fact]
    public void ParseStoryMap_ClassifiesSectionFidelity()
    {
        const string json = """
        {
          "title": "Watershed",
          "nodes": {
            "n1": { "type": "storycover", "title": "Cover" },
            "n2": { "type": "swipe", "title": "Before/after" },
            "n3": { "type": "embedexternal", "title": "Video" },
            "n4": { "type": "webmap", "title": "Map" }
          }
        }
        """;

        var outcome = _parser.ParseStoryMap(json, "story.json");

        Assert.True(outcome.Succeeded, outcome.Error);
        var result = outcome.Result!;
        Assert.Equal(EsriContentKind.StoryMap, result.Kind);
        Assert.Equal(ImportFidelity.Clean, result.Rows.Single(r => r.SourceName == "Cover").Fidelity);
        Assert.Equal(ImportFidelity.Degrade, result.Rows.Single(r => r.SourceName == "Before/after").Fidelity);
        Assert.Equal(ImportFidelity.Drop, result.Rows.Single(r => r.SourceName == "Video").Fidelity);
        Assert.Equal(ImportFidelity.Clean, result.Rows.Single(r => r.SourceName == "Map").Fidelity);
    }

    [Fact]
    public void BundledSamples_ParseSuccessfullyThroughTheRealParser()
    {
        Assert.True(_parser.ParseWebMap(EsriImportSampleDocuments.WebMap, EsriImportSampleDocuments.WebMapFileName).Succeeded);
        Assert.True(_parser.ParseDashboard(EsriImportSampleDocuments.Dashboard, EsriImportSampleDocuments.DashboardFileName).Succeeded);
        Assert.True(_parser.ParseStoryMap(EsriImportSampleDocuments.StoryMap, EsriImportSampleDocuments.StoryMapFileName).Succeeded);
    }
}
