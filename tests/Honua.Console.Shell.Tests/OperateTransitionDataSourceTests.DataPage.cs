using Honua.Console.Shell.Pages;
using Microsoft.AspNetCore.Components;

namespace Honua.Console.Shell.Tests;

public sealed partial class OperateTransitionDataSourceTests
{
    [Fact]
    public async Task DataPage_ReadsEachConnectionServiceLayerListOnce()
    {
        string[] connections = ["11111111-1111-1111-1111-111111111111", "22222222-2222-2222-2222-222222222222"];
        var responses = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["/api/v1/admin/connections/"] = $$$"""
                {"success":true,"data":[{"connectionId":"{{{connections[0]}}}","name":"First","host":"db","databaseName":"honua","provider":"PostgreSQL/PostGIS"},{"connectionId":"{{{connections[1]}}}","name":"Second","host":"other","databaseName":"other","provider":"PostgreSQL/PostGIS"}]}
                """,
            ["/api/v1/admin/services/?page=1&pageSize=100"] = """
                {"success":true,"data":{"items":[{"serviceName":"planning","layerCount":1,"enabledProtocols":["FeatureServer"]},{"serviceName":"public","layerCount":1}],"page":1,"pageSize":100,"total":2,"layerTotal":2}}
                """,
        };
        var layerPaths = connections
            .SelectMany(connection => new[] { "", "&serviceName=planning", "&serviceName=public" }
                .Select(scope => $"/api/v1/admin/connections/{connection}/layers/?page=1&pageSize=100{scope}"))
            .ToArray();
        foreach (var path in layerPaths)
        {
            responses[path] = path.EndsWith("planning", StringComparison.Ordinal) && path.Contains(connections[0], StringComparison.Ordinal)
                ? """{"success":true,"data":{"items":[{"storageLayerId":7,"layerName":"Target parcels","schema":"public","table":"parcels","geometryType":"Polygon","serviceName":"planning","enabled":true}],"page":1,"pageSize":100,"total":1}}"""
                : """{"success":true,"data":{"items":[],"page":1,"pageSize":100,"total":0}}""";
        }
        var handler = new RecordingJsonFixtureHandler(responses);

        var html = await RenderPageAsync<OperateDataPage>(CreateServerDataSource(handler), ParameterView.Empty);

        Assert.Contains("Target parcels", html);
        Assert.Contains("FeatureServer", html);
        Assert.All(layerPaths, path => Assert.Single(handler.RequestedPaths, requested => requested == path));
        Assert.DoesNotContain(handler.RequestedPaths, path => path.Contains("/settings", StringComparison.Ordinal));
    }
}
