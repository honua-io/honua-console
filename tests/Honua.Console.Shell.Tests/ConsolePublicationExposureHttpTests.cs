using System.Net;
using System.Net.Http.Json;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;
using Xunit.Abstractions;

namespace Honua.Console.Shell.Tests;

public sealed class ConsolePublicationExposureHttpTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("allowed")]
    [InlineData("denied")]
    [InlineData("absent")]
    public async Task LoadedExposureUsesObservedConnectionReadsAndSeparatesDenialFromAbsence(string scenario)
    {
        using var handler = new ExposureHandler(scenario);
        using var http = new HttpClient(handler);
        using var client = new HonuaAdminOperateHttpClient(http, new(new Uri("https://fixture.invalid")));
        var view = await new HonuaServerOperateTransitionDataSource(client).GetLayersViewAsync();
        var selection = new ConsolePublicationLayerContext();
        selection.CompleteRead(selection.BeginRead("connection-one-layer-7"), view.FindExposures("connection-one-layer-7"));
        Assert.Equal(scenario == "denied", view.CapabilityStates.Any(state => state.StatusCode == 403));
        Assert.Equal(1, handler.Paths.Count(path => path == "/api/v1/admin/connections/"));
        Assert.Contains("/api/v1/admin/connections/connection-one/layers/?page=1&pageSize=100", handler.Paths);
        Assert.Contains("/api/v1/admin/connections/connection-one/layers/?page=1&pageSize=100&serviceName=z-parcels", handler.Paths);
        var connectionReads = handler.Paths.Count(path => path.Contains("/connections/", StringComparison.Ordinal));
        if (scenario != "allowed")
        {
            Assert.Null(selection.Selection);
            Assert.Empty(view.FindExposures("connection-one-layer-7"));
            return;
        }
        Assert.Equal("z-parcels", selection.Selection!.ServiceName);
        var ancestry = await new ConsolePublicationPathReader(new ServerConsolePublicationNavigationSource(client))
            .ReadAsync(selection.Selection, CancellationToken.None);
        Assert.True(ancestry.IsAllowed, ancestry.Message);
        Assert.Equal(["Services", "Public", "Parcels", "Bridges"], ancestry.Value!.Select(node => node.Label));
        Assert.Equal("LineString", ancestry.Value![^1].GeometryType);
        Assert.Equal(connectionReads, handler.Paths.Count(path => path.Contains("/connections/", StringComparison.Ordinal)));
        Assert.Contains("/api/v1/admin/services/?page=1&pageSize=100&q=z-parcels", handler.Paths);
    }

    [Fact]
    public async Task ColdNavigationThroughTenThousandLayersKeepsAncestryLookupBounded()
    {
        using var handler = new ExposureHandler("allowed", 10000);
        using var http = new HttpClient(handler);
        using var client = new HonuaAdminOperateHttpClient(http, new(new Uri("https://fixture.invalid")));
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        var view = await new HonuaServerOperateTransitionDataSource(client).GetLayersViewAsync();
        var selection = new ConsolePublicationLayerContext();
        selection.CompleteRead(selection.BeginRead("connection-one-layer-7"), view.FindExposures("connection-one-layer-7"));
        var initialDuration = elapsed.Elapsed;
        var initialRequests = handler.Paths.Count;
        Assert.NotNull(selection.Selection);
        Assert.Equal(200, handler.Paths.Count(path => path.Contains("/layers/", StringComparison.Ordinal)));
        var ancestry = await new ConsolePublicationPathReader(new ServerConsolePublicationNavigationSource(client))
            .ReadAsync(selection.Selection!, CancellationToken.None);
        elapsed.Stop();
        output.WriteLine($"Cold 10000-layer connection: initial exposure {initialRequests} requests, {initialDuration.TotalMilliseconds:F2} ms; ancestry {handler.Paths.Count - initialRequests} request, {(elapsed.Elapsed - initialDuration).TotalMilliseconds:F2} ms. Synthetic HTTP, shared budget {ConsoleReadBudget.Default.TotalMilliseconds} ms.");
        Assert.True(ancestry.IsAllowed, ancestry.Message);
        Assert.Equal("Bridges", ancestry.Value![^1].Label);
        Assert.Equal(initialRequests + 1, handler.Paths.Count);
        Assert.True(elapsed.Elapsed < ConsoleReadBudget.Default);
    }

    private sealed class ExposureHandler(string scenario, int layerCount = 1) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            lock (Paths) Paths.Add(uri.PathAndQuery);
            if (uri.AbsolutePath == "/api/v1/admin/connections/")
                return Task.FromResult(Envelope(new[] { new { connectionId = Guid.Empty, name = "Connection one" } }));
            if (uri.AbsolutePath == "/api/v1/admin/services/")
                return Task.FromResult(Envelope(new
                {
                    items = new[] { new { serviceName = "z-parcels", title = "Parcels", folderId = "public", folderName = "Public", layerCount } },
                    page = 1, pageSize = 100, total = 1
                }));
            Assert.Equal("/api/v1/admin/connections/connection-one/layers/", uri.AbsolutePath);
            if (scenario == "denied") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
            var page = int.Parse(uri.Query.TrimStart('?').Split('&').Single(part => part.StartsWith("page=", StringComparison.Ordinal))[5..], System.Globalization.CultureInfo.InvariantCulture);
            var layers = layerCount == 1 ? new[] { scenario == "absent" ? 8 : 7 }
                : Enumerable.Range((page - 1) * 100, Math.Min(100, layerCount - (page - 1) * 100)).ToArray();
            return Task.FromResult(Envelope(new
            {
                items = layers.Select(id => new { layerId = id, layerName = id == 7 ? "Bridges" : $"Layer {id}", serviceName = "z-parcels", geometryType = "LineString" }).ToArray(),
                page, pageSize = 100, total = layerCount
            }));
        }

        private static HttpResponseMessage Envelope(object data) => new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { success = true, data })
        };
    }
}
