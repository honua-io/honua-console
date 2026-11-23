using System.Collections.Concurrent;
using System.Net;
using Honua.Console.Contracts;
using Honua.Console.Shell.Services;

namespace Honua.Console.Shell.Tests;

public sealed class OperateTransitionLayerScopeTests
{
    private static readonly Guid ConnectionId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherConnectionId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task WorkspaceSkipsLayerReadsForServicesWithoutLayers()
    {
        var handler = new RecordingHandler(Fixtures("/api/v1/admin/services/?page=1&pageSize=100"));

        var workspace = await new HonuaServerOperateTransitionDataSource(CreateClient(handler)).GetWorkspaceAsync();

        Assert.Contains(workspace.ResourceEdits, resource => resource.Name == "Console Parcels");
        AssertOnlyLayerServicesRead(handler);
    }

    [Fact]
    public async Task SingleLayerViewRetainsLegacyPublicationWithoutStorageBinding()
    {
        var handler = new RecordingHandler(Fixtures(
            $"/api/v1/admin/services/?page=1&pageSize=100&connectionId={ConnectionId:D}"));

        var view = await new HonuaServerOperateTransitionDataSource(CreateClient(handler))
            .GetLayerViewAsync($"{ConnectionId:D}-layer-7");

        Assert.Contains(view.Services, service => service.Layers.Any(layer => layer.Name == "Console Parcels"));
        AssertOnlyLayerServicesRead(handler);
    }

    [Fact]
    public async Task SingleResourceViewReadsOnlyItsOwnConnectionLayers()
    {
        var fixtures = Fixtures($"/api/v1/admin/services/?page=1&pageSize=100&connectionId={ConnectionId:D}");
        fixtures["/api/v1/admin/connections/"] = $$"""
            {"success":true,"data":[{"connectionId":"{{ConnectionId}}","name":"Live PostGIS","provider":"PostgreSQL/PostGIS","isActive":true,"healthStatus":"Healthy","storageType":"managed"},{"connectionId":"{{OtherConnectionId}}","name":"Archive","provider":"PostgreSQL/PostGIS","isActive":true,"healthStatus":"Healthy","storageType":"managed"}]}
            """;
        var handler = new RecordingHandler(fixtures);

        var view = await new HonuaServerOperateTransitionDataSource(CreateClient(handler))
            .GetResourceViewAsync($"{ConnectionId:D}-layer-7");

        var resource = Assert.Single(view.ResourceEdits);
        Assert.Equal($"{ConnectionId:D}-layer-7", resource.ResourceId);
        Assert.DoesNotContain(handler.Requests, path => path.Contains($"{OtherConnectionId:D}/layers", StringComparison.Ordinal));
        AssertOnlyLayerServicesRead(handler);
        Assert.DoesNotContain(handler.Requests, path => path.Contains("storageLayerId=", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ScopedDetailRetainsPublishedLayerWithoutServiceSummary(bool dataset)
    {
        var fixtures = Fixtures($"/api/v1/admin/services/?page=1&pageSize=100&connectionId={ConnectionId:D}");
        fixtures[$"/api/v1/admin/services/?page=1&pageSize=100&connectionId={ConnectionId:D}"] = EmptyLayerPage;
        fixtures["/api/v1/admin/connections/"] = $$"""
            {"success":true,"data":[{"connectionId":"{{ConnectionId}}","name":"Live PostGIS","provider":"PostgreSQL/PostGIS","isActive":true,"healthStatus":"Healthy","storageType":"managed"},{"connectionId":"{{OtherConnectionId}}","name":"Archive","provider":"PostgreSQL/PostGIS","isActive":true,"healthStatus":"Healthy","storageType":"managed"}]}
            """;
        fixtures[$"/api/v1/admin/connections/{ConnectionId}/layers/?page=1&pageSize=100"] = """
            {"success":true,"data":{"items":[{"storageLayerId":7,"layerName":"Console Parcels","geometryType":"Polygon","enabled":true,"serviceName":"default"},{"storageLayerId":8,"layerName":"Console Roads","geometryType":"Line","enabled":true,"serviceName":"default"}],"page":1,"pageSize":100,"total":2}}
            """;
        var handler = new RecordingHandler(fixtures);
        var source = new HonuaServerOperateTransitionDataSource(CreateClient(handler));
        var resourceId = $"{ConnectionId:D}-layer-7";

        if (dataset)
        {
            var view = await source.GetResourceViewAsync(resourceId);
            var resource = Assert.Single(view.ResourceEdits);
            Assert.Equal(resourceId, resource.ResourceId);
            Assert.Equal(new[] { "default" }, resource.BlastRadius.Services);
            Assert.Empty(view.CapabilityStates);
        }
        else
        {
            var view = await source.GetLayerViewAsync(resourceId);
            var service = Assert.Single(view.Services);
            Assert.Equal("default", service.Name);
            Assert.Equal(resourceId, Assert.Single(service.Layers).CanonicalResourceId);
            Assert.Empty(view.CapabilityStates);
        }

        var layerRead = Assert.Single(handler.Requests.Where(path => path.Contains("/layers/", StringComparison.Ordinal)));
        Assert.Equal($"/api/v1/admin/connections/{ConnectionId}/layers/?page=1&pageSize=100", layerRead);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ScopedDetailRetainsDefaultAndNamedPublications(bool dataset)
    {
        var fixtures = Fixtures($"/api/v1/admin/services/?page=1&pageSize=100&connectionId={ConnectionId:D}");
        fixtures[$"/api/v1/admin/connections/{ConnectionId}/layers/?page=1&pageSize=100"] = """
            {"success":true,"data":{"items":[{"storageLayerId":7,"layerName":"Console Parcels","geometryType":"Polygon","enabled":true,"serviceName":"default"},{"storageLayerId":8,"layerName":"Console Roads","geometryType":"Line","enabled":true,"serviceName":"default"}],"page":1,"pageSize":100,"total":2}}
            """;
        var handler = new RecordingHandler(fixtures);
        var source = new HonuaServerOperateTransitionDataSource(CreateClient(handler));
        var resourceId = $"{ConnectionId:D}-layer-7";

        if (dataset)
        {
            var view = await source.GetResourceViewAsync(resourceId);
            var resource = Assert.Single(view.ResourceEdits);
            Assert.Equal(resourceId, resource.ResourceId);
            Assert.Equal(new[] { "default", "planning" }, resource.BlastRadius.Services);
            Assert.Empty(view.CapabilityStates);
        }
        else
        {
            var view = await source.GetLayerViewAsync(resourceId);
            Assert.Equal(new[] { "default", "planning" }, view.Services.Where(service => service.Layers.Count > 0).Select(service => service.Name).OrderBy(name => name));
            Assert.All(view.Services.Where(service => service.Layers.Count > 0), service => Assert.Equal(resourceId, Assert.Single(service.Layers).CanonicalResourceId));
            Assert.Empty(view.CapabilityStates);
        }

        Assert.Equal(2, handler.Requests.Count(path => path.Contains("/layers/", StringComparison.Ordinal)));
        AssertOnlyLayerServicesRead(handler);
    }

    private static void AssertOnlyLayerServicesRead(RecordingHandler handler)
    {
        Assert.Contains(handler.Requests, path => path.EndsWith("serviceName=planning", StringComparison.Ordinal));
        Assert.DoesNotContain(handler.Requests, path => path.Contains("serviceName=geoprocessing", StringComparison.Ordinal));
    }

    private static HonuaAdminOperateHttpClient CreateClient(HttpMessageHandler handler) => new(
        new HttpClient(handler) { BaseAddress = new Uri("https://server.example") },
        new HonuaAdminOperateClientOptions(new Uri("https://server.example"), "test-api-key"));

    private static Dictionary<string, string> Fixtures(string servicesPath) => new(StringComparer.Ordinal)
    {
        ["/api/v1/admin/connections/"] = $$"""
            {"success":true,"data":[{"connectionId":"{{ConnectionId}}","name":"Live PostGIS","provider":"PostgreSQL/PostGIS","isActive":true,"healthStatus":"Healthy","storageType":"managed"}]}
            """,
        [$"/api/v1/admin/connections/{ConnectionId}/layers/?page=1&pageSize=100"] = EmptyLayerPage,
        [$"/api/v1/admin/connections/{ConnectionId}/layers/?page=1&pageSize=100&serviceName=planning"] = """
            {"success":true,"data":{"items":[{"storageLayerId":7,"layerName":"Console Parcels","geometryType":"Polygon","enabled":true,"serviceName":"planning"}],"page":1,"pageSize":100,"total":1}}
            """,
        [servicesPath] = """
            {"success":true,"data":{"items":[{"serviceName":"planning","layerCount":1,"enabledProtocols":["FeatureServer"]},{"serviceName":"geoprocessing","layerCount":0,"enabledProtocols":["GeoprocessingServer"]}],"page":1,"pageSize":100,"total":2}}
            """,
    };

    private const string EmptyLayerPage = """
        {"success":true,"data":{"items":[],"page":1,"pageSize":100,"total":0}}
        """;

    private sealed class RecordingHandler(IReadOnlyDictionary<string, string> responses) : HttpMessageHandler
    {
        public ConcurrentQueue<string> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var key = request.RequestUri?.PathAndQuery ?? string.Empty;
            Requests.Enqueue(key);
            var found = responses.TryGetValue(key, out var json);
            return Task.FromResult(new HttpResponseMessage(found ? HttpStatusCode.OK : HttpStatusCode.NotFound)
            {
                Content = new StringContent(found ? json! : """{"success":false,"message":"missing fixture"}""",
                    System.Text.Encoding.UTF8, "application/json")
            });
        }
    }
}
