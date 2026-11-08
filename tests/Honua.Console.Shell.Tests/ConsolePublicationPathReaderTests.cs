using System.Net;
using System.Net.Http.Json;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.Shell.Tests;

public sealed class ConsolePublicationPathReaderTests
{
    [Theory]
    [InlineData("unique", 4)]
    [InlineData("missing", 0)]
    [InlineData("ambiguous", 0)]
    [InlineData("layer-denied", 0)]
    [InlineData("catalog-denied", 0)]
    public async Task ReadAsync_ConnectionScopedLayerUsesUniqueAuthorizedService(string scenario, int expectedNodes)
    {
        using var handler = new PathHandler(scenario);
        using var http = new HttpClient(handler);
        using var client = new HonuaAdminOperateHttpClient(http, new(new Uri("https://fixture.invalid")));
        var result = await new ConsolePublicationPathReader(new ServerConsolePublicationNavigationSource(client))
            .ReadAsync("/operate/layers/connection-one-layer-101", CancellationToken.None);
        Assert.Equal(expectedNodes, result.Value?.Count ?? 0);
        if (expectedNodes > 0)
        {
            Assert.Equal(["Services", "Public", "Parcels", "Bridges"], result.Value!.Select(node => node.Label));
            Assert.Equal(4, handler.Paths.Count);
            Assert.Equal(2, handler.Paths.Count(path => path.Contains("/layers/", StringComparison.Ordinal)));
        }
        Assert.All(handler.Paths.Where(path => path.Contains("/services/", StringComparison.Ordinal)),
            path => Assert.Equal("/api/v1/admin/services/?page=1&pageSize=100&q=z-parcels", path));
        Assert.DoesNotContain(handler.Paths, path => path.Contains("serviceName=", StringComparison.Ordinal));
    }

    private sealed class PathHandler(string scenario) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            Paths.Add(uri.PathAndQuery);
            if (uri.AbsolutePath.EndsWith("/connections/", StringComparison.Ordinal))
                return Task.FromResult(Envelope(new[] { new { connectionId = Guid.Empty, name = "connection-one" } }));
            if (uri.AbsolutePath.Contains("/layers/", StringComparison.Ordinal))
            {
                if (scenario == "layer-denied") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
                var page = uri.Query.Contains("page=2", StringComparison.Ordinal) ? 2 : 1;
                var layers = page == 1
                    ? Enumerable.Range(1, 100).Select(id => new { layerId = id, layerName = "Other", serviceName = "other", geometryType = "Point" }).ToArray()
                    : [new { layerId = 101, layerName = "Bridges", serviceName = "z-parcels", geometryType = "LineString" }];
                return Task.FromResult(Envelope(new { items = layers, page, pageSize = 100, total = 101 }));
            }
            Assert.Equal("/api/v1/admin/services/", uri.AbsolutePath);
            if (scenario == "catalog-denied") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
            var count = scenario == "missing" ? 0 : scenario == "ambiguous" ? 2 : 1;
            var services = Enumerable.Range(0, count).Select(_ => new
            { serviceName = "z-parcels", title = "Parcels", folderId = "public", folderName = "Public", layerCount = 1 }).ToArray();
            return Task.FromResult(Envelope(new { items = services, page = 1, pageSize = 100, total = count }));
        }

        private static HttpResponseMessage Envelope(object data) => new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { success = true, data })
        };
    }
}
