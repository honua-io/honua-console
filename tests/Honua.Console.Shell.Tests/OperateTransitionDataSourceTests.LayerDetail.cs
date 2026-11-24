using Honua.Console.Shell.Pages;
using Microsoft.AspNetCore.Components;

namespace Honua.Console.Shell.Tests;

public sealed partial class OperateTransitionDataSourceTests
{
    [Fact]
    public async Task LayerDetail_ReadsOnlyTheSelectedConnectionsServiceExposures()
    {
        const string connection = "11111111-1111-1111-1111-111111111111";
        var responses = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["/api/v1/admin/connections/"] = $$$"""
                {"success":true,"data":[{"connectionId":"{{{connection}}}","name":"Selected","host":"db","databaseName":"honua","provider":"PostgreSQL/PostGIS"},{"connectionId":"22222222-2222-2222-2222-222222222222","name":"Unrelated","host":"other","databaseName":"other","provider":"PostgreSQL/PostGIS"}]}
                """,
            ["/api/v1/admin/services/?page=1&pageSize=100"] = """
                {"success":true,"data":{"items":[{"serviceName":"planning","layerCount":1},{"serviceName":"public","layerCount":1}],"page":1,"pageSize":100,"total":2,"layerTotal":2}}
                """,
            [$"/api/v1/admin/connections/{connection}/layers/?page=1&pageSize=100"] = """
                {"success":true,"data":{"items":[],"page":1,"pageSize":100,"total":0}}
                """,
        };
        foreach (var service in new[] { "planning", "public" })
        {
            responses[$"/api/v1/admin/connections/{connection}/layers/?page=1&pageSize=100&serviceName={service}"] = $$$"""
                {"success":true,"data":{"items":[{"storageLayerId":7,"layerName":"Target parcels","schema":"public","table":"parcels","geometryType":"Polygon","serviceName":"{{{service}}}","enabled":true}],"page":1,"pageSize":100,"total":1}}
                """;
        }
        var handler = new RecordingJsonFixtureHandler(responses);
        var html = await RenderPageAsync<OperateLayerDetailPage>(CreateServerDataSource(handler),
            ParameterView.FromDictionary(new Dictionary<string, object?> { ["ResourceId"] = connection + "-layer-7" }));

        Assert.Contains("Target parcels", html);
        Assert.Contains("/operate/services/planning/settings", html);
        Assert.Contains("/operate/services/public/settings", html);
        Assert.Equal(responses.Keys.Order(), handler.RequestedPaths.Order());
    }
}
