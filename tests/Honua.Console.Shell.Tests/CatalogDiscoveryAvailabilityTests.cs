using System.Net;
using Honua.Console.Contracts;
using Honua.Console.Shell.Services;

namespace Honua.Console.Shell.Tests;

public sealed class CatalogDiscoveryAvailabilityTests
{
    [Fact]
    public async Task AbsentRegistryOffersSetupGuidanceWithoutReclassifyingIndividualRecords()
    {
        using var http = new HttpClient(new StatusHandler(HttpStatusCode.NotFound));
        using var client = new HonuaCatalogDiscoveryHttpClient(http,
            new HonuaCatalogDiscoveryClientOptions(new Uri("https://server.example")));
        var data = new HonuaServerCatalogDiscoveryDataSource(client);
        var registry = await data.LoadRegistryAsync("current");
        var state = Assert.Single(registry.CapabilityStates);
        Assert.Equal("Not configured", state.State);
        Assert.Contains("Ask an administrator", state.Detail);
        var endpoint = await data.LoadEndpointAsync("current", "missing");
        var item = await data.LoadItemAsync("current", "missing", "unknown");
        Assert.NotEqual("Not configured", Assert.Single(endpoint.CapabilityStates).State);
        Assert.NotEqual("Not configured", Assert.Single(item.CapabilityStates).State);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task RegistryErrorsDoNotBecomeSetupStates(HttpStatusCode status)
    {
        using var http = new HttpClient(new StatusHandler(status));
        using var client = new HonuaCatalogDiscoveryHttpClient(http,
            new HonuaCatalogDiscoveryClientOptions(new Uri("https://server.example")));
        var result = await new HonuaServerCatalogDiscoveryDataSource(client).LoadRegistryAsync("current");
        Assert.NotEqual("Not configured", Assert.Single(result.CapabilityStates).State);
    }

    private sealed class StatusHandler(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status));
    }
}
