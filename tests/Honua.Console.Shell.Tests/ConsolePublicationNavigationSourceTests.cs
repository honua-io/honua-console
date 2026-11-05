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
        using var handler = new NavigationHandler(_ => SuccessfulEnvelope(Enumerable.Range(0, 500)
            .Select(index => new { serviceName = $"service-{index}", layerCount = 100 })));
        using var http = new HttpClient(handler);
        using var client = new HonuaAdminOperateHttpClient(http, new(new Uri("https://fixture.invalid")));
        var source = new ServerConsolePublicationNavigationSource(client);
        var read = await source.ReadServicesAsync(CancellationToken.None);
        Assert.True(read.IsAllowed, read.Message);
        Assert.Equal(500, read.Value!.Count);
        Assert.Equal("/api/v1/admin/services/", Assert.Single(handler.Paths));
    }

    [Fact]
    public async Task LazyReadUsesOnlyRequestedServiceAndPreservesConnectionScopedLayerIdentity()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        using var handler = new NavigationHandler(_ => SuccessfulEnvelope(new[]
        {
            new { layerId = 7, layerName = "Parcels", serviceName = "public roads", geometryType = "Polygon" },
            new { layerId = 8, layerName = "Other", serviceName = "private", geometryType = "Point" }
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
        Assert.All(handler.Paths, path => Assert.EndsWith("/layers/?serviceName=public%20roads", path));
    }

    [Fact]
    public async Task RequestedServiceScopeSupportsResponsesWithoutOptionalServiceName()
    {
        using var handler = new NavigationHandler(_ => SuccessfulEnvelope(new[] { new { layerId = 9, layerName = "Roads" } }));
        using var http = new HttpClient(handler);
        using var client = new HonuaAdminOperateHttpClient(http, new(new Uri("https://fixture.invalid")));
        var read = await new ServerConsolePublicationNavigationSource(client).ReadLayersAsync("roads", ["legacy-connection"], CancellationToken.None);
        Assert.True(read.IsAllowed, read.Message);
        Assert.Equal("Roads", Assert.Single(read.Value!).Name);
        Assert.Contains("serviceName=roads", Assert.Single(handler.Paths));
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
            : SuccessfulEnvelope(new[] { new { layerId = 7, layerName = "Roads", serviceName = "transport" } }));
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
        var read = await new ServerConsolePublicationNavigationSource(client).ReadServicesAsync(CancellationToken.None);
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
            : SuccessfulEnvelope(new[] { new { layerId = 1, layerName = "Roads", serviceName = "transport" } }));
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
