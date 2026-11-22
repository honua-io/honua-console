using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components;

namespace Honua.Console.Shell.Tests;

public sealed partial class OperateTransitionDataSourceTests
{
    [Fact]
    public async Task ServicesPage_ReadsOnlyServiceSummariesWithoutLayerOrSettingsFanOut()
    {
        var responses = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["/api/v1/admin/services/?page=1&pageSize=100"] = """
                {"success":true,"data":{"items":[{"serviceName":"planning","title":"Planning service","layerCount":3,"enabledProtocols":["FeatureServer","OgcFeatures"]},{"serviceName":"empty-svc","layerCount":0,"enabledProtocols":["MapServer"]}],"page":1,"pageSize":100,"total":2,"layerTotal":3}}
                """
        };
        var handler = new RecordingJsonFixtureHandler(responses);

        var html = await RenderPageAsync<OperateServicesPage>(CreateServerDataSource(handler), ParameterView.Empty);

        Assert.Contains("Planning service", html);
        Assert.Contains("FeatureServer, OgcFeatures", html);
        Assert.Contains(">3<", html);
        Assert.Contains("empty-svc", html);
        Assert.Contains(">0<", html);
        Assert.Equal(["/api/v1/admin/services/?page=1&pageSize=100"], handler.RequestedPaths);
    }

    [Fact]
    public async Task GetServiceDirectoryViewAsync_PreservesSummaryLayerCountAndEndpoints()
    {
        var responses = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["/api/v1/admin/services/?page=1&pageSize=100"] = """
                {"success":true,"data":{"items":[{"serviceName":"planning","title":"Planning","publicName":"planning","folderId":"folder-1","folderName":"ops","layerCount":4,"enabledProtocols":["FeatureServer","OgcFeatures"]}],"page":1,"pageSize":100,"total":1,"layerTotal":4}}
                """
        };
        var handler = new RecordingJsonFixtureHandler(responses);
        var dataSource = CreateServerDataSource(handler);

        var view = await dataSource.GetServiceDirectoryViewAsync();

        var service = Assert.Single(view.Services);
        Assert.Equal("planning", service.Name);
        Assert.Equal("Planning", service.DisplayName);
        Assert.Equal("folder-1", service.FolderId);
        Assert.Equal("ops", service.FolderName);
        Assert.Equal(4, service.LayerCount);
        Assert.Empty(service.Layers);
        Assert.Equal("Configured", service.RuntimeStatus);
        var featureServer = Assert.Single(service.Endpoints, slot => slot.Name == "FeatureServer");
        Assert.Contains("/rest/services/ops/planning/FeatureServer", featureServer.Url, StringComparison.Ordinal);
        Assert.Empty(view.CapabilityStates);
        Assert.Equal(["/api/v1/admin/services/?page=1&pageSize=100"], handler.RequestedPaths);
    }

    [Fact]
    public async Task GetServiceDirectoryViewAsync_WhenServicesEndpointReturnsNoRows_DerivesServicesFromPublishedLayers()
    {
        const string connectionId = "11111111-1111-1111-1111-111111111111";
        var responses = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["/api/v1/admin/services/?page=1&pageSize=100"] = """
                {"success":true,"data":{"items":[],"page":1,"pageSize":100,"total":0,"layerTotal":0}}
                """,
            ["/api/v1/admin/connections/"] = $$$"""
                {"success":true,"data":[{"connectionId":"{{{connectionId}}}","name":"Primary","host":"db","databaseName":"honua","provider":"PostgreSQL/PostGIS"}]}
                """,
            [$"/api/v1/admin/connections/{connectionId}/layers/?page=1&pageSize=100"] = """
                {"success":true,"data":{"items":[{"storageLayerId":9,"layerName":"Unprojected parcels","schema":"public","table":"parcels","geometryType":"Polygon","serviceName":"legacy-svc","enabled":true}],"page":1,"pageSize":100,"total":1}}
                """
        };
        var handler = new RecordingJsonFixtureHandler(responses);
        var dataSource = CreateServerDataSource(handler);

        var view = await dataSource.GetServiceDirectoryViewAsync();

        var service = Assert.Single(view.Services);
        Assert.Equal("legacy-svc", service.Name);
        Assert.Equal(1, service.LayerCount);
        Assert.Single(service.Layers);
        Assert.Equal("Configured", service.RuntimeStatus);
        Assert.Empty(view.CapabilityStates);
        Assert.DoesNotContain(handler.RequestedPaths, path => path.Contains("/settings", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetServiceDirectoryViewAsync_EnvironmentSwitchMidRead_ReturnsUnavailable()
    {
        var envA = new Uri("https://env-a.example.test/");
        var envB = new Uri("https://env-b.example.test/");
        var profiles = new InMemoryConsoleEnvironmentProfileStore([
            new() { Id = "env-a", ServerBaseUri = envA },
            new() { Id = "env-b", ServerBaseUri = envB }
        ]);
        await profiles.ActivateProfileAsync("env-a");

        var responses = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["/api/v1/admin/services/?page=1&pageSize=100"] = """
                {"success":true,"data":{"items":[{"serviceName":"planning","layerCount":2,"enabledProtocols":["FeatureServer"]}],"page":1,"pageSize":100,"total":1,"layerTotal":2}}
                """
        };
        var switched = false;
        using var handler = new SwitchOnRequestHandler(responses, async () =>
        {
            if (switched)
            {
                return;
            }

            switched = true;
            await profiles.ActivateProfileAsync("env-b");
        });
        using var http = new HttpClient(handler) { BaseAddress = envA };
        var client = new HonuaAdminOperateHttpClient(http, new HonuaAdminOperateClientOptions(envA, null));
        var dataSource = new HonuaServerOperateTransitionDataSource(client, profiles);

        var view = await dataSource.GetServiceDirectoryViewAsync();

        Assert.Empty(view.Services);
        Assert.Equal([ConsoleEnvironmentRead.Unavailable], view.CapabilityStates);
    }

    private sealed class SwitchOnRequestHandler(
        IReadOnlyDictionary<string, string> responses,
        Func<Task> onRequest) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await onRequest().ConfigureAwait(false);
            var path = request.RequestUri?.PathAndQuery ?? string.Empty;
            if (!responses.TryGetValue(path, out var body))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
            }

            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
            };
        }
    }
}
