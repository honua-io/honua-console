namespace Honua.Console.Shell.Services;

// These are source-format documents run through the parser, not fabricated mapping rows.
public static class EsriImportSampleDocuments
{
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

    public const string InstantAppFileName = "public-works-instant-app.json";

    public const string InstantApp = """
    {
      "templateId": "instant/sidebar",
      "appItemId": "a1b2c3d4e5f6",
      "title": "Public Works Viewer",
      "values": {
        "webmap": "9f8e7d6c5b4a",
        "header": true,
        "search": true,
        "legend": true,
        "home": true,
        "zoom": true,
        "basemapToggle": true,
        "share": true,
        "measure": true,
        "bookmarks": true,
        "layerList": true,
        "popup": true,
        "sidebarPanel": true,
        "expressions": true,
        "filter": true,
        "theme": { "themeColor": "#0a7" },
        "splash": { "title": "Welcome", "content": "<b>Custom HTML splash</b>" },
        "customUrlParam": true,
        "measureUnsupportedExample": false
      }
    }
    """;

    public const string NotebookFileName = "parcel-sync.ipynb";

    public const string Notebook = """
    {
      "cells": [
        { "cell_type": "markdown", "metadata": {}, "source": ["# Nightly parcel sync\n", "Refreshes the parcels feature layer from the county source."] },
        { "cell_type": "code", "metadata": { "tags": ["parameters"] }, "source": ["county_url = \"https://services.arcgis.com/abc/County/FeatureServer/0\"\n", "max_records = 5000\n"] },
        { "cell_type": "code", "metadata": {}, "source": ["import arcpy\n", "from arcgis.gis import GIS\n", "gis = GIS(\"home\")\n"] },
        { "cell_type": "code", "metadata": {}, "source": ["import pandas as pd\n", "df = pd.DataFrame()\n", "print(df.shape)"] },
        { "cell_type": "code", "metadata": {}, "source": ["arcpy.management.RepairGeometry(\"parcels\")\n"] },
        { "cell_type": "raw", "metadata": {}, "source": ["nbconvert template directive"] }
      ],
      "metadata": {
        "kernelspec": { "display_name": "source notebook Python 3 (Advanced)", "name": "python3" },
        "esriNotebookRuntime": "source notebook Python 3 Advanced",
        "parameters": { "county_url": "https://services.arcgis.com/abc/County/FeatureServer/0", "max_records": 5000 },
        "schedule": { "cron": "0 2 * * *", "timezone": "Pacific/Honolulu" }
      },
      "nbformat": 4,
      "nbformat_minor": 5
    }
    """;
}
