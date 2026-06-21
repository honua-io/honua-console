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
    public void ParseInstantApp_SidebarTemplate_MapsCapabilitiesAndClassifiesFidelity()
    {
        const string json = """
        {
          "templateId": "instant/sidebar",
          "title": "Public Works Viewer",
          "values": {
            "webmap": "9f8e7d6c",
            "search": true,
            "legend": true,
            "sidebarPanel": true,
            "expressions": true,
            "theme": { "themeColor": "#0a7" },
            "splash": { "title": "Welcome" },
            "share": false
          }
        }
        """;

        var outcome = _parser.ParseInstantApp(json, "app.json");

        Assert.True(outcome.Succeeded, outcome.Error);
        var result = outcome.Result!;
        Assert.Equal(EsriContentKind.InstantApp, result.Kind);
        Assert.Equal("honua.app-package.v1", result.TargetSchema);
        Assert.Equal("public-works-viewer", result.SuggestedName);
        Assert.Contains("Sidebar", result.SourceLabel, StringComparison.Ordinal);

        // Primary web map is a manual binding row (it imports via the Web Map surface).
        var map = result.Rows.Single(r => r.SourceType == "webmap");
        Assert.Equal(ImportFidelity.Manual, map.Fidelity);
        Assert.Equal("9f8e7d6c", map.BoundResource);

        // Core viewer capabilities convert clean.
        Assert.Equal(ImportFidelity.Clean, result.Rows.Single(r => r.SourceName == "Search").Fidelity);
        Assert.Equal(ImportFidelity.Clean, result.Rows.Single(r => r.SourceName == "Legend").Fidelity);

        // Sidebar-specific extras: panel converts clean, Arcade info degrades.
        Assert.Equal(ImportFidelity.Clean, result.Rows.Single(r => r.SourceName == "Sidebar panel").Fidelity);
        Assert.Equal(ImportFidelity.Degrade, result.Rows.Single(r => r.SourceName == "Arcade info").Fidelity);

        // Theme degrades; custom splash drops.
        Assert.Equal(ImportFidelity.Degrade, result.Rows.Single(r => r.SourceName == "Theme").Fidelity);
        var splash = result.Rows.Single(r => r.SourceName == "Splash screen");
        Assert.Equal(ImportFidelity.Drop, splash.Fidelity);
        Assert.False(splash.Included);

        // A disabled toggle (share=false) is skipped entirely.
        Assert.DoesNotContain(result.Rows, r => r.SourceName == "Share");

        Assert.True(result.DropCount >= 1);
        Assert.True(result.DegradeCount >= 2);
    }

    [Fact]
    public void ParseInstantApp_UnknownTemplate_DegradesToBasicViewer()
    {
        const string json = """
        { "templateId": "instant/nearby", "title": "Find Nearby", "values": { "webmap": "abc", "search": true } }
        """;

        var outcome = _parser.ParseInstantApp(json, "app.json");

        Assert.True(outcome.Succeeded, outcome.Error);
        var result = outcome.Result!;
        Assert.Contains("Basic", result.SourceLabel, StringComparison.Ordinal);
        // Basic viewer has no sidebar-specific rows.
        Assert.DoesNotContain(result.Rows, r => r.SourceName == "Sidebar panel");
        Assert.Equal(ImportFidelity.Clean, result.Rows.Single(r => r.SourceName == "Search").Fidelity);
    }

    [Fact]
    public void ParseInstantApp_WebScene_DropsAsUnsupportedPrimaryContent()
    {
        const string json = """
        { "templateId": "instant/basic", "title": "3D Viewer", "values": { "webscene": "scene123", "search": true } }
        """;

        var outcome = _parser.ParseInstantApp(json);

        Assert.True(outcome.Succeeded, outcome.Error);
        var scene = outcome.Result!.Rows.Single(r => r.SourceType == "webscene");
        Assert.Equal(ImportFidelity.Drop, scene.Fidelity);
        Assert.False(scene.Included);
    }

    [Fact]
    public void ParseInstantApp_InvalidJson_ReturnsFailure()
    {
        var outcome = _parser.ParseInstantApp("{ not json", "bad.json");

        Assert.False(outcome.Succeeded);
        Assert.NotNull(outcome.Error);
    }

    [Fact]
    public void BundledSamples_ParseSuccessfullyThroughTheRealParser()
    {
        Assert.True(_parser.ParseWebMap(EsriImportSampleDocuments.WebMap, EsriImportSampleDocuments.WebMapFileName).Succeeded);
        Assert.True(_parser.ParseDashboard(EsriImportSampleDocuments.Dashboard, EsriImportSampleDocuments.DashboardFileName).Succeeded);
        Assert.True(_parser.ParseStoryMap(EsriImportSampleDocuments.StoryMap, EsriImportSampleDocuments.StoryMapFileName).Succeeded);
        Assert.True(_parser.ParseInstantApp(EsriImportSampleDocuments.InstantApp, EsriImportSampleDocuments.InstantAppFileName).Succeeded);
    }
}
