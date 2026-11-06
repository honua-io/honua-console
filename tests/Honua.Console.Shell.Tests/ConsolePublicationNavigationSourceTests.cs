using System.Net;
using System.Net.Http.Json;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.Shell.Tests;

public sealed class ConsolePublicationNavigationSourceTests
{
    [Fact]
    public async Task InitialReadForFiveHundredServicesDoesNotFetchConnectionsOrLayers()
    {
        using var handler = new NavigationHandler(_ => SuccessfulEnvelope(new
        {
            items = Enumerable.Range(0, 100).Select(index => new { serviceName = $"service-{index}", layerCount = 10 }),
            page = 1,
            pageSize = 100,
            total = 500,
            layerTotal = 5000
        }));
        using var http = new HttpClient(handler);
        using var client = new HonuaAdminOperateHttpClient(http, new(new Uri("https://fixture.invalid")));
        var source = new ServerConsolePublicationNavigationSource(client);
        var read = await source.ReadServicesAsync(HonuaAdminCatalogQuery.First, CancellationToken.None);
        Assert.True(read.IsAllowed, read.Message);
        Assert.Equal(100, read.Value!.Items.Length);
        Assert.Equal(500, read.Value.Total);
        Assert.Equal(5000, read.Value.LayerTotal);
        Assert.Equal("/api/v1/admin/services/?page=1&pageSize=100", Assert.Single(handler.Paths));
    }

    [Fact]
    public async Task NameQueryIsSentOnTheFirstPageOnly()
    {
        using var handler = new NavigationHandler(_ => SuccessfulEnvelope(new
        {
            items = new[] { new { serviceName = "needle-service", layerCount = 1 } },
            page = 1,
            pageSize = 100,
            total = 1,
            layerTotal = 1
        }));
        using var http = new HttpClient(handler);
        using var client = new HonuaAdminOperateHttpClient(http, new(new Uri("https://fixture.invalid")));
        var read = await new ServerConsolePublicationNavigationSource(client).ReadServicesAsync(
            HonuaAdminCatalogQuery.First with { NameQuery = "needle-service" }, CancellationToken.None);
        Assert.True(read.IsAllowed, read.Message);
        Assert.Equal("needle-service", Assert.Single(read.Value!.Items).ServiceName);
        Assert.Equal("/api/v1/admin/services/?page=1&pageSize=100&q=needle-service", Assert.Single(handler.Paths));
    }

    [Fact]
    public async Task LazyReadUsesOnlyRequestedServiceAndPreservesConnectionScopedLayerIdentity()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        using var handler = new NavigationHandler(_ => SuccessfulEnvelope(new
        {
            items = new[]
            {
                new { layerId = 7, layerName = "Parcels", serviceName = "public roads", geometryType = "Polygon" },
                new { layerId = 8, layerName = "Other", serviceName = "private", geometryType = "Point" }
            },
            page = 1,
            pageSize = 100,
            total = 2
        }));
        using var http = new HttpClient(handler);
        using var client = new HonuaAdminOperateHttpClient(http, new(new Uri("https://fixture.invalid")));
        var source = new ServerConsolePublicationNavigationSource(client);
        var read = await source.ReadLayersAsync("public roads", [first.ToString("D"), second.ToString("D")], CancellationToken.None);
        Assert.True(read.IsAllowed, read.Message);
        Assert.Equal(2, read.Value!.Count);
        Assert.Contains(read.Value, layer => layer.ResourceId == OperateResourceIdentity.ForPublishedLayer(first.ToString("D"), 7));
        Assert.Contains(read.Value, layer => layer.ResourceId == OperateResourceIdentity.ForPublishedLayer(second.ToString("D"), 7));
        Assert.All(read.Value, layer => Assert.Equal("Parcels", layer.Name));
        Assert.Equal(2, handler.Paths.Count);
        Assert.All(handler.Paths, path => Assert.EndsWith("/layers/?page=1&pageSize=100&serviceName=public%20roads", path));
    }

    [Fact]
    public async Task RequestedServiceScopeSupportsResponsesWithoutOptionalServiceName()
    {
        using var handler = new NavigationHandler(_ => SuccessfulEnvelope(new
        {
            items = new[] { new { layerId = 9, layerName = "Roads" } },
            page = 1,
            pageSize = 100,
            total = 1
        }));
        using var http = new HttpClient(handler);
        using var client = new HonuaAdminOperateHttpClient(http, new(new Uri("https://fixture.invalid")));
        var read = await new ServerConsolePublicationNavigationSource(client).ReadLayersAsync("roads", ["legacy-connection"], CancellationToken.None);
        Assert.True(read.IsAllowed, read.Message);
        Assert.Equal("Roads", Assert.Single(read.Value!).Name);
        Assert.Contains("page=1&pageSize=100&serviceName=roads", Assert.Single(handler.Paths));
    }

    [Fact]
    public async Task EmptyConnectionIdsKeepDistinctCanonicalNameAndHostKeys()
    {
        HonuaAdminConnectionSummary[] summaries =
        [
            new() { ConnectionId = Guid.Empty, Name = "Legacy First" },
            new() { ConnectionId = Guid.Empty, Host = "second.example.test" }
        ];
        using var handler = new NavigationHandler(request => request.RequestUri!.AbsolutePath.EndsWith("/connections/", StringComparison.Ordinal)
            ? SuccessfulEnvelope(summaries)
            : SuccessfulEnvelope(new
            {
                items = new[] { new { layerId = 7, layerName = "Roads", serviceName = "transport" } },
                page = 1,
                pageSize = 100,
                total = 1
            }));
        using var http = new HttpClient(handler);
        using var client = new HonuaAdminOperateHttpClient(http, new(new Uri("https://fixture.invalid")));
        var source = new ServerConsolePublicationNavigationSource(client);
        var connections = await source.ReadConnectionsAsync(CancellationToken.None);
        Assert.True(connections.IsAllowed, connections.Message);
        Assert.Equal(["legacy-first", "second-example-test"], connections.Value!);
        var layerPageConnections = await new HonuaServerOperateTransitionDataSource(client).GetConnectionsViewAsync();
        Assert.Equal(layerPageConnections.Connections.Select(connection => connection.Id), connections.Value!);
        var layers = await source.ReadLayersAsync("transport", connections.Value!, CancellationToken.None);
        Assert.True(layers.IsAllowed, layers.Message);
        Assert.Equal(["legacy-first-layer-7", "second-example-test-layer-7"], layers.Value!.Select(layer => layer.ResourceId));
        Assert.Equal(2, handler.Paths.Count(path => path.Contains("/layers/", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task MissingConnectionProjectionCannotBecomeAnAllowedEmptyService()
    {
        using var handler = new NavigationHandler(_ => throw new InvalidOperationException("No layer request should be sent."));
        using var http = new HttpClient(handler);
        using var client = new HonuaAdminOperateHttpClient(http, new(new Uri("https://fixture.invalid")));
        var read = await new ServerConsolePublicationNavigationSource(client).ReadLayersAsync("roads", [], CancellationToken.None);
        Assert.False(read.IsAllowed);
        Assert.Equal(OperateSectionStatus.Unsupported, read.Status);
        Assert.Null(read.Value);
        Assert.Empty(handler.Paths);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, OperateSectionStatus.Forbidden)]
    [InlineData(HttpStatusCode.NotImplemented, OperateSectionStatus.Unsupported)]
    [InlineData(HttpStatusCode.ServiceUnavailable, OperateSectionStatus.Unavailable)]
    public async Task DeniedReadsNeverBecomeAnAllowedEmptyTree(HttpStatusCode response, OperateSectionStatus status)
    {
        using var handler = new NavigationHandler(_ => new(response));
        using var http = new HttpClient(handler);
        using var client = new HonuaAdminOperateHttpClient(http, new(new Uri("https://fixture.invalid")));
        var read = await new ServerConsolePublicationNavigationSource(client).ReadServicesAsync(HonuaAdminCatalogQuery.First, CancellationToken.None);
        Assert.False(read.IsAllowed);
        Assert.Equal(status, read.Status);
        Assert.Null(read.Value);
    }

    [Fact]
    public async Task PartialConnectionReadKeepsReachableLayersAndReportsIncompleteCoverage()
    {
        var deniedConnection = Guid.NewGuid();
        using var handler = new NavigationHandler(request => request.RequestUri!.AbsolutePath.Contains(deniedConnection.ToString("D"))
            ? new(HttpStatusCode.Forbidden)
            : SuccessfulEnvelope(new
            {
                items = new[] { new { layerId = 1, layerName = "Roads", serviceName = "transport" } },
                page = 1,
                pageSize = 100,
                total = 1
            }));
        using var http = new HttpClient(handler);
        using var client = new HonuaAdminOperateHttpClient(http, new(new Uri("https://fixture.invalid")));
        var read = await new ServerConsolePublicationNavigationSource(client).ReadLayersAsync("transport", [Guid.NewGuid().ToString("D"), deniedConnection.ToString("D")], CancellationToken.None);
        Assert.True(read.IsAllowed, read.Message);
        Assert.True(read.PartialResult);
        Assert.Single(read.Value!);
        Assert.Contains("partial", read.Message);
    }

    private static HttpResponseMessage SuccessfulEnvelope(object data) => new(HttpStatusCode.OK)
    {
        Content = JsonContent.Create(new { success = true, data })
    };

    private sealed class NavigationHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (Paths) Paths.Add(request.RequestUri!.PathAndQuery);
            return Task.FromResult(respond(request));
        }
    }
}
