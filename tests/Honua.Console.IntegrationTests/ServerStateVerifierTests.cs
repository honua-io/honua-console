using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Honua.Console.IntegrationTests;

public sealed class ServerStateVerifierTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task RegisteredLayerReadsTheServerPageAndPreservesServiceScope(int requestedLayer)
    {
        var paths = new List<string>();
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var app = builder.Build();
        app.MapGet("/api/v1/admin/connections/{id}/layers", (HttpContext context, string id) =>
        {
            Assert.Equal("connection", id);
            Assert.Equal("parks west", context.Request.Query["serviceName"].ToString());
            Assert.Equal("fixture-key", context.Request.Headers["X-API-Key"].ToString());
            paths.Add(context.Request.QueryString.Value!);
            var page = int.Parse(context.Request.Query["page"].FirstOrDefault() ?? "1");
            return Results.Json(new
            {
                data = new
                {
                    items = new[] { new { layerId = page, layerName = $"Layer {page}", serviceName = "parks west", table = "parcels", enabled = true } },
                    page, pageSize = 1, total = 2
                }
            });
        });
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var verifier = new ServerStateVerifier(new Uri(address), "fixture-key");
        var layer = await verifier.GetRegisteredLayerAsync("connection", "parks west", requestedLayer);
        Assert.NotNull(layer);
        Assert.Equal($"Layer {requestedLayer}", layer.LayerName);
        Assert.Equal("parcels", layer.Table);
        Assert.Equal(requestedLayer, paths.Count);
    }
}
