using System.Text.Json;
using Honua.Console.Shell.Models;

namespace Honua.Console.Native.Core.Tests;

public sealed class StudioCanonicalPackageTests
{
    [Theory]
    [InlineData("{\"format\":17,\"createdAt\":42}")]
    [InlineData("{\"format\":\"honua_map_package.v1\",\"createdAt\":42}")]
    public void Map_MalformedOptionalCanonicalFields_DoNotThrow(string json)
    {
        using var document = JsonDocument.Parse(json);
        StudioMapPackageMapper.ApplyEnvelopeBody(StudioMapPackageMapper.CreateTemplate(), document.RootElement);
    }

    [Fact]
    public void Map_ChangedIdentityDoesNotReusePreviousLocator()
    {
        using var binding = JsonDocument.Parse("""
            {"sourceId":"service:old/1","protocol":"geoservices_feature_service","locator":{"serviceId":"old","layerId":1}}
            """);
        var state = StudioMapPackageMapper.CreateTemplate();
        var layer = new StudioMapLayerEditor
        {
            SourceRef = "service:old/1",
            BoundServiceId = "old",
            BoundLayerId = "1",
            SourceBinding = binding.RootElement.Clone()
        };
        Assert.True(layer.HasResolvedSource);
        layer.SourceRef = "service:new/2";
        layer.BoundServiceId = "new";
        layer.BoundLayerId = "2";
        Assert.False(layer.HasCanonicalSource);
        state.Layers.Add(layer);
        var locator = StudioMapPackageMapper.BuildEnvelopeBody(state).GetProperty("sourceBindings")[0].GetProperty("locator");
        Assert.Equal("new", locator.GetProperty("serviceId").GetString());
        Assert.Equal("2", locator.GetProperty("layerId").GetString());
        Assert.False(locator.TryGetProperty("url", out _));
    }

    [Fact]
    public void Map_CanonicalBindingsStyleAndViewportSurviveEditorRoundTrip()
    {
        using var document = JsonDocument.Parse("""
            {"mapPackageId":"map_real","format":"honua_map_package.v1","status":"Draft",
             "createdAt":"2026-09-25T00:00:00Z","themeId":"theme_real",
             "sourceBindings":[{"sourceId":"stations","protocol":"geoservices_feature_service",
               "locator":{"url":"https://server.example/rest/services/weather/FeatureServer","serviceId":"weather","layerId":"3"},
               "metadata":{"owner":"weather-team"}}],
             "mapSpec":{"version":8,"sources":{"stations":{"type":"geojson","data":"https://server.example/stations.geojson"}},
               "layers":[{"id":"stations","type":"circle","source":"stations"}],"metadata":{"existing":"retained"}},
             "initialView":{"bbox":[0,0,1,1],"crs":"EPSG:4326","zoom":4},
             "styleRefs":[{"styleId":"station-style"}],"popupBindings":[{"sourceId":"stations","fieldName":"name"}]}
            """);
        var state = StudioMapPackageMapper.CreateTemplate();
        StudioMapPackageMapper.ApplyEnvelopeBody(state, document.RootElement);
        state.Title = "Observed stations";
        state.Basemap = "basemap:streets";
        state.Layers[0].Filter = "active = 1";
        Assert.True(StudioMapPublishEvaluator.Evaluate(state).CanPublish);

        var package = StudioMapPackageMapper.BuildEnvelopeBody(state);
        Assert.Equal("map_real", package.GetProperty("mapPackageId").GetString());
        Assert.Equal(1, package.GetProperty("mapSpec").GetProperty("layers").GetArrayLength());
        Assert.Equal("retained", package.GetProperty("mapSpec").GetProperty("metadata").GetProperty("existing").GetString());
        Assert.Equal(4, package.GetProperty("initialView").GetProperty("zoom").GetInt32());
        Assert.Equal("weather-team", package.GetProperty("sourceBindings")[0].GetProperty("metadata").GetProperty("owner").GetString());
        Assert.Equal("active = 1", package.GetProperty("sourceBindings")[0].GetProperty("filter").GetString());
        Assert.Equal("station-style", package.GetProperty("styleRefs")[0].GetProperty("styleId").GetString());

        var reopened = StudioMapPackageMapper.CreateTemplate();
        StudioMapPackageMapper.ApplyEnvelopeBody(reopened, package);
        Assert.Equal(state.PackageId, reopened.PackageId);
        Assert.Equal(state.PackageCreatedAt, reopened.PackageCreatedAt);
        Assert.Equal("Observed stations", reopened.Title);
        Assert.True(Assert.Single(reopened.Layers).HasResolvedSource);
        Assert.Equal("name", reopened.Layers[0].PopupFields);
    }

    [Fact]
    public void Map_OpaqueReferenceRemainsEditableButCannotPublishOrAcquireInventedLocator()
    {
        var state = StudioMapPackageMapper.CreateTemplate();
        state.Title = "Unresolved";
        state.Basemap = "basemap:streets";
        state.InitialExtent = "0,0,1,1";
        state.Layers.Add(new StudioMapLayerEditor { SourceRef = "content:opaque@v1", Title = "Keep this intent" });
        var body = StudioMapPackageMapper.BuildEnvelopeBody(state);
        Assert.Empty(body.GetProperty("sourceBindings").EnumerateArray());
        var reopened = StudioMapPackageMapper.CreateTemplate();
        StudioMapPackageMapper.ApplyEnvelopeBody(reopened, body);
        Assert.Equal("content:opaque@v1", Assert.Single(reopened.Layers).SourceRef);
        Assert.False(StudioMapPublishEvaluator.Evaluate(reopened).CanPublish);
    }

    [Fact]
    public void App_CanonicalRuntimeConfigurationPreservesAuthoredPagesAndOtherSettings()
    {
        using var original = JsonDocument.Parse("""
            {"appPackageId":"app_real","format":"honua_app_package.v1","targetSdk":"honua-sdk-js","status":"Draft",
             "createdAt":"2026-09-25T00:00:00Z","bundleArtifactId":"bundle_real",
             "runtimeConfig":{"customSetting":42,"title":"Old","pages":[]}}
            """);
        var state = StudioAppPackageMapper.CreateTemplate();
        StudioAppPackageMapper.ApplyEnvelopeBody(state, original.RootElement);
        state.Title = "New app";
        state.Pages.Add(new StudioAppPageState { Route = "/observations", Title = "Observations", ContentBinding = "content:observations@v2" });
        var package = StudioAppPackageMapper.BuildEnvelopeBody(state);
        Assert.Equal("app_real", package.GetProperty("appPackageId").GetString());
        Assert.Equal("bundle_real", package.GetProperty("bundleArtifactId").GetString());
        Assert.Equal(42, package.GetProperty("runtimeConfig").GetProperty("customSetting").GetInt32());
        var reopened = StudioAppPackageMapper.CreateTemplate();
        StudioAppPackageMapper.ApplyEnvelopeBody(reopened, package);
        Assert.Equal("New app", reopened.Title);
        Assert.Equal("content:observations@v2", Assert.Single(reopened.Pages).ContentBinding);
        Assert.Equal(state.PackageCreatedAt, reopened.PackageCreatedAt);
    }
}
