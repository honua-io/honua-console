using Honua.Console.Shell.Pages;
using Microsoft.AspNetCore.Components;

namespace Honua.Console.Shell.Tests;

public sealed partial class OperateTransitionDataSourceTests
{
    [Fact]
    public async Task ServiceDetail_ReadsOnlyTheRequestedServiceSettingsAndLayers()
    {
        const string connection = "11111111-1111-1111-1111-111111111111";
        var responses = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["/api/v1/admin/connections/"] = $$$"""
                {"success":true,"data":[{"connectionId":"{{{connection}}}","name":"Selected","host":"db","databaseName":"honua","provider":"PostgreSQL/PostGIS"}]}
                """,
            ["/api/v1/admin/services/?page=1&pageSize=100"] = """
                {"success":true,"data":{"items":[{"serviceName":"planning","layerCount":1,"enabledProtocols":["FeatureServer"]},{"serviceName":"public","layerCount":1,"enabledProtocols":["FeatureServer"]}],"page":1,"pageSize":100,"total":2,"layerTotal":2}}
                """,
            [$"/api/v1/admin/connections/{connection}/layers/?page=1&pageSize=100&serviceName=planning"] = """
                {"success":true,"data":{"items":[{"storageLayerId":7,"layerName":"Target parcels","schema":"public","table":"parcels","geometryType":"Polygon","serviceName":"planning","enabled":true}],"page":1,"pageSize":100,"total":1}}
                """,
            ["/api/v1/admin/services/planning/settings"] = """
                {"success":true,"data":{"serviceName":"planning","enabledProtocols":["FeatureServer"],"availableProtocols":["FeatureServer","OgcFeatures"]}}
                """,
        };
        var handler = new RecordingJsonFixtureHandler(responses);

        var html = await RenderPageAsync<OperateServiceDetailPage>(
            CreateServerDataSource(handler),
            ParameterView.FromDictionary(new Dictionary<string, object?> { ["ServiceName"] = "planning" }));

        Assert.Contains("Target parcels", html);
        Assert.DoesNotContain("Service Not Found", html);
        Assert.Equal(responses.Keys.Order(), handler.RequestedPaths.Order());
    }

    [Fact]
    public async Task ServiceDetail_MissingServiceSkipsUnrelatedSettingsAndLayerFanout()
    {
        const string connection = "11111111-1111-1111-1111-111111111111";
        var responses = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["/api/v1/admin/connections/"] = $$$"""
                {"success":true,"data":[{"connectionId":"{{{connection}}}","name":"Selected","host":"db","databaseName":"honua","provider":"PostgreSQL/PostGIS"}]}
                """,
            ["/api/v1/admin/services/?page=1&pageSize=100"] = """
                {"success":true,"data":{"items":[{"serviceName":"planning","layerCount":1,"enabledProtocols":["FeatureServer"]},{"serviceName":"public","layerCount":1,"enabledProtocols":["FeatureServer"]}],"page":1,"pageSize":100,"total":2,"layerTotal":2}}
                """,
            [$"/api/v1/admin/connections/{connection}/layers/?page=1&pageSize=100&serviceName=missing"] = """
                {"success":true,"data":{"items":[],"page":1,"pageSize":100,"total":0}}
                """,
        };
        var handler = new RecordingJsonFixtureHandler(responses);

        var view = await CreateServerDataSource(handler).GetServiceViewAsync("missing");

        Assert.Empty(view.Services);
        Assert.Equal(responses.Keys.Order(), handler.RequestedPaths.Order());
    }

    [Fact]
    public async Task ServiceDetail_DiscoversPublishedServiceAbsentFromRegistry()
    {
        const string connection = "11111111-1111-1111-1111-111111111111";
        var responses = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["/api/v1/admin/connections/"] = $$$"""
                {"success":true,"data":[{"connectionId":"{{{connection}}}","name":"Selected","host":"db","databaseName":"honua","provider":"PostgreSQL/PostGIS"}]}
                """,
            ["/api/v1/admin/services/?page=1&pageSize=100"] = """
                {"success":true,"data":{"items":[],"page":1,"pageSize":100,"total":0,"layerTotal":0}}
                """,
            [$"/api/v1/admin/connections/{connection}/layers/?page=1&pageSize=100&serviceName=planning"] = """
                {"success":true,"data":{"items":[{"storageLayerId":7,"layerName":"Target parcels","schema":"public","table":"parcels","geometryType":"Polygon","serviceName":"planning","enabled":true}],"page":1,"pageSize":100,"total":1}}
                """,
        };
        var handler = new RecordingJsonFixtureHandler(responses);

        var html = await RenderPageAsync<OperateServiceDetailPage>(
            CreateServerDataSource(handler),
            ParameterView.FromDictionary(new Dictionary<string, object?> { ["ServiceName"] = "planning" }));

        Assert.Contains("Target parcels", html);
        Assert.DoesNotContain("Service Not Found", html);
        Assert.Equal(responses.Keys.Order(), handler.RequestedPaths.Order());
    }
}
