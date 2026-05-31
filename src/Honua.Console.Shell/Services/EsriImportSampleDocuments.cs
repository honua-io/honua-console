namespace Honua.Console.Shell.Services;

/// <summary>
/// Bundled, illustrative Esri content documents the import surfaces parse on first load so a reviewer sees a
/// populated mapping table without having to paste their own export first. These are real Esri-shaped JSON
/// documents run through the real <see cref="EsriContentImportParser"/> — the mapping/fidelity they produce
/// is deterministic parser output, not fabricated UI data (Console Patterns Charter section 11). Replacing
/// the sample by pasting/uploading a real export re-parses against the same code path.
/// </summary>
public static class EsriImportSampleDocuments
{
    /// <summary>An ArcGIS Web Map JSON sample exercising clean / degrade / drop / manual layer fidelity.</summary>
    public const string WebMapFileName = "public-works-webmap.json";

    public const string WebMap = """
    {
      "title": "Public Works Overview",
      "operationalLayers": [
        { "id": "parcels", "title": "Parcels", "layerType": "ArcGISFeatureLayer", "url": "https://services.arcgis.com/abc/arcgis/rest/services/Parcels/FeatureServer/0", "popupInfo": { "title": "{PARCELID}" } },
        { "id": "roads", "title": "Roads", "layerType": "ArcGISFeatureLayer", "url": "https://services.arcgis.com/abc/arcgis/rest/services/Roads/FeatureServer/0" },
        { "id": "hydrants", "title": "Hydrants", "layerType": "ArcGISFeatureLayer" },
        { "id": "landuse", "title": "Land use", "layerType": "ArcGISFeatureLayer", "url": "https://services.arcgis.com/abc/arcgis/rest/services/LandUse/FeatureServer/0",
          "layerDefinition": { "drawingInfo": {
            "renderer": { "type": "uniqueValue", "field1": "USE_CODE" },
            "labelingInfo": [ { "labelExpressionInfo": { "expression": "$feature.USE_CODE + ' / ' + $feature.ZONE" } } ]
          } } },
        { "id": "imagery", "title": "Imagery", "layerType": "ArcGISTiledMapServiceLayer", "url": "https://tiles.arcgis.com/abc/Imagery/MapServer" },
        { "id": "heatmap311", "title": "Heatmap · 311 calls", "layerType": "ArcGISFeatureLayer", "url": "https://services.arcgis.com/abc/arcgis/rest/services/Calls311/FeatureServer/0",
          "layerDefinition": { "drawingInfo": { "renderer": { "type": "heatmap" } } } }
      ],
      "baseMap": { "title": "Imagery", "baseMapLayers": [ { "id": "world-imagery", "layerType": "ArcGISTiledMapServiceLayer", "url": "https://tiles.arcgis.com/World/MapServer" } ] },
      "bookmarks": [ { "name": "Downtown" }, { "name": "North" }, { "name": "Harbor" }, { "name": "Airport" } ],
      "widgets": [ { "type": "TimeSlider", "title": "Time slider widget" }, { "type": "Legend", "title": "Legend" } ]
    }
    """;

    /// <summary>An ArcGIS Dashboard JSON sample exercising clean / degrade / drop element fidelity.</summary>
    public const string DashboardFileName = "q3-ops-dashboard.json";

    public const string Dashboard = """
    {
      "title": "Q3 Operations",
      "headerPanel": { "type": "header", "name": "Header" },
      "widgets": [
        { "type": "indicator", "name": "Indicator · total", "datasetId": "parcels_2024" },
        { "type": "serialChart", "name": "Serial chart", "datasetId": "parcels_2024" },
        { "type": "pieChart", "name": "Pie chart · use", "datasetId": "parcels_2024" },
        { "type": "gauge", "name": "Gauge · capacity", "datasetId": "obs_stations" },
        { "type": "list", "name": "List · recent", "datasetId": "fire_observations" },
        { "type": "mapWidget", "name": "Map", "datasetId": "parcels_2024" },
        { "type": "richText", "name": "Rich text · embed" }
      ],
      "selectors": [
        { "type": "categorySelector", "name": "Category selector", "datasetId": "use_code" }
      ]
    }
    """;

    /// <summary>An ArcGIS StoryMap export sample exercising clean / degrade / drop section fidelity.</summary>
    public const string StoryMapFileName = "watershed-health-story.json";

    public const string StoryMap = """
    {
      "title": "Watershed Health 2024",
      "nodes": {
        "n1": { "type": "storycover", "title": "Cover" },
        "n2": { "type": "text", "title": "Intro" },
        "n3": { "type": "webmap", "title": "Map: parcels" },
        "n4": { "type": "immersive", "title": "Sidecar: trends" },
        "n5": { "type": "swipe", "title": "Swipe: before/after" },
        "n6": { "type": "imagegallery", "title": "Media gallery" },
        "n7": { "type": "embedexternal", "title": "Embedded video" }
      }
    }
    """;
}
