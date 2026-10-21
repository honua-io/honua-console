using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free coverage for layer and service discovery metadata. The pinned server does not
/// register those routes, so the live client returns Unsupported and issues no HTTP. The
/// missing-binding operation still names HONUA_SERVER_BASE_URL.
/// </summary>
public sealed class DiscoveryMetadataOperationTests
{
    private static readonly Uri BaseAddress = new("https://honua.test");

    [Fact]
    public async Task UnregisteredDiscoveryRoutes_FailClosedWithoutHttp()
    {
        var calls = 0;
        var operation = new HonuaServerConsoleDiscoveryMetadataOperation(CreateClient(_ =>
        {
            calls++;
            throw new InvalidOperationException("unregistered route must not send HTTP");
        }));

        var layer = await operation.GetLayerDiscoveryAsync(42);
        var service = await operation.GetServiceDiscoveryAsync("parcels");
        var saveLayer = await operation.SaveLayerDiscoveryAsync(42, new ConsoleDiscoveryMetadata { Bound = true });
        var saveService = await operation.SaveServiceDiscoveryAsync("parcels", new ConsoleDiscoveryMetadata { Bound = true });

        Assert.Equal(0, calls);
        Assert.False(layer.Bound);
        Assert.False(service.Bound);
        Assert.Contains(UnregisteredServerRoutes.Detail, layer.Detail, StringComparison.Ordinal);
        Assert.False(saveLayer.Succeeded);
        Assert.Equal(UnregisteredServerRoutes.State, saveLayer.State);
        Assert.False(saveService.Succeeded);
        Assert.Equal(UnregisteredServerRoutes.State, saveService.State);
    }

    [Fact]
    public async Task Unsupported_NeverCallsNetwork_AndReturnsMissingBinding()
    {
        var operation = new UnsupportedConsoleDiscoveryMetadataOperation();

        var layerRead = await operation.GetLayerDiscoveryAsync(1);
        var layerWrite = await operation.SaveLayerDiscoveryAsync(1, new ConsoleDiscoveryMetadata { Bound = true });
        var serviceRead = await operation.GetServiceDiscoveryAsync("svc");
        var serviceWrite = await operation.SaveServiceDiscoveryAsync("svc", new ConsoleDiscoveryMetadata { Bound = true });

        Assert.False(layerRead.Bound);
        Assert.Contains("HONUA_SERVER_BASE_URL", layerRead.Detail!, StringComparison.Ordinal);
        Assert.False(layerWrite.Succeeded);
        Assert.Equal("Missing binding", layerWrite.State);
        Assert.False(serviceRead.Bound);
        Assert.False(serviceWrite.Succeeded);
        Assert.Equal("Missing binding", serviceWrite.State);
        Assert.Contains("HONUA_SERVER_BASE_URL", serviceWrite.Detail!, StringComparison.Ordinal);
    }

    private static HonuaAdminDiscoveryMetadata SampleMetadata() => new()
    {
        Title = "Parcels",
        Description = "Cadastral parcels.",
        Keywords = new[] { "cadastre", "parcels" },
        Themes = new[] { "boundaries" },
        Language = "en",
        License = "CC-BY-4.0",
        Attribution = "County GIS",
        Publisher = "County GIS Department",
        ContactPoint = new HonuaAdminDiscoveryContactPoint { Name = "GIS Help Desk", Email = "gis@county.example", Url = "https://county.example/gis" },
        Links = new[] { new HonuaAdminDiscoveryLink { Href = "https://county.example/metadata", Rel = "describedby", Type = "text/html", Title = "Metadata", Hreflang = "en" } },
    };

    private static HttpResponseMessage Ok<T>(T data) =>
        new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { success = true, data, timestamp = DateTimeOffset.UtcNow })
        };

    private static IHonuaAdminOperateClient CreateClient(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var httpClient = new HttpClient(new StubHandler(responder)) { BaseAddress = BaseAddress };
        return new HonuaAdminOperateHttpClient(httpClient, new HonuaAdminOperateClientOptions(BaseAddress, "test-key"));
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _ = request.Content?.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
            return Task.FromResult(responder(request));
        }
    }
}
