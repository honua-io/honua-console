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
    [InlineData("catalog-denied", 0)]
    public async Task ReadAsync_LoadedLayerExposureUsesUniqueAuthorizedService(string scenario, int expectedNodes)
    {
        using var handler = new PathHandler(scenario);
        using var http = new HttpClient(handler);
        using var client = new HonuaAdminOperateHttpClient(http, new(new Uri("https://fixture.invalid")));
        var result = await new ConsolePublicationPathReader(new ServerConsolePublicationNavigationSource(client))
            .ReadAsync(new("connection-one-layer-101", "z-parcels", "Bridges"), CancellationToken.None);
        Assert.Equal(expectedNodes, result.Value?.Count ?? 0);
        if (expectedNodes > 0)
        {
            Assert.Equal(["Services", "Public", "Parcels", "Bridges"], result.Value!.Select(node => node.Label));
            Assert.Single(handler.Paths);
        }
        Assert.All(handler.Paths.Where(path => path.Contains("/services/", StringComparison.Ordinal)),
            path => Assert.Equal("/api/v1/admin/services/?page=1&pageSize=100&q=z-parcels", path));
        Assert.DoesNotContain(handler.Paths, path => path.Contains("serviceName=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReadAsync_MissingServiceNameDoesNotSearchTheWholeCatalog()
    {
        using var handler = new PathHandler("unique");
        using var http = new HttpClient(handler);
        using var client = new HonuaAdminOperateHttpClient(http, new(new Uri("https://fixture.invalid")));
        var result = await new ConsolePublicationPathReader(new ServerConsolePublicationNavigationSource(client))
            .ReadAsync(new("connection-one-layer-101", " ", "Bridges"), CancellationToken.None);
        Assert.True(result.IsAllowed);
        Assert.Empty(result.Value!);
        Assert.Empty(handler.Paths);
    }

    private sealed class PathHandler(string scenario) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            Paths.Add(uri.PathAndQuery);
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
