using System.Net;
using System.Text;
using Honua.Console.Contracts;
using Honua.Console.Shell.Services;

namespace Honua.Console.Shell.Tests;

public sealed class CatalogDiscoveryEnvironmentTests
{
    [Theory]
    [InlineData("Public Works", "Public Works")]
    [InlineData(null, "env-west")]
    public async Task RegistryProjectsCanonicalEnvironmentFields(string? name, string display)
    {
        var payload = $$$"""
            {"success":true,"data":{"environmentId":"env-west",
            "environmentName":{{{System.Text.Json.JsonSerializer.Serialize(name)}}},
            "publicHost":"https://server.example","endpoints":[],"autoDefaultCount":2,"optInCount":3}}
            """;
        using var http = new HttpClient(new ResponseHandler(payload));
        using var client = CreateClient(http);

        var result = await new HonuaServerCatalogDiscoveryDataSource(client).LoadRegistryAsync("env-west");

        var registry = Assert.IsType<Honua.Console.Shell.Models.CatalogDiscoveryRegistryView>(result.Registry);
        Assert.Empty(result.CapabilityStates);
        Assert.Equal("env-west", registry.EnvironmentId);
        Assert.Equal(name, registry.EnvironmentName);
        Assert.Equal(display, registry.DisplayEnvironment);
        Assert.Equal(2, registry.AutoDefaultCount);
        Assert.Equal(3, registry.OptInCount);
    }

    [Fact]
    public async Task RegistryWithoutCanonicalEnvironmentIdRemainsUnavailable()
    {
        const string payload = """{"success":true,"data":{"workspaceId":"env-west","workspaceName":"Old","endpoints":[]}}""";
        using var http = new HttpClient(new ResponseHandler(payload));
        using var client = CreateClient(http);

        var result = await new HonuaServerCatalogDiscoveryDataSource(client).LoadRegistryAsync("env-west");

        Assert.Null(result.Registry);
        Assert.NotEmpty(result.CapabilityStates);
    }

    [Theory]
    [InlineData("registry", "/api/v1/console/catalog-endpoints/env%20west")]
    [InlineData("endpoint", "/api/v1/console/catalog-endpoints/env%20west/ogc")]
    [InlineData("item", "/api/v1/console/catalog-endpoints/env%20west/ogc/items/a3bf-0214")]
    public async Task ReadsPreserveEnvironmentAndItemIdentifiers(string operation, string path)
    {
        using var http = new HttpClient(new ResponseHandler("", request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal(path, request.RequestUri!.AbsolutePath);
        }, HttpStatusCode.ServiceUnavailable));
        using var client = CreateClient(http);

        var issue = operation switch
        {
            "registry" => (await client.GetRegistryAsync("env west")).Issue,
            "endpoint" => (await client.GetEndpointAsync("env west", "ogc")).Issue,
            "item" => (await client.GetItemAsync("env west", "ogc", "a3bf-0214")).Issue,
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };

        Assert.NotNull(issue);
        Assert.Contains("{environmentId}", issue.Contract, StringComparison.Ordinal);
        Assert.DoesNotContain("workspace", issue.Contract, StringComparison.OrdinalIgnoreCase);
    }

    private static HonuaCatalogDiscoveryHttpClient CreateClient(HttpClient http) =>
        new(http, new HonuaCatalogDiscoveryClientOptions(new Uri("https://server.example")));

    private sealed class ResponseHandler(
        string payload,
        Action<HttpRequestMessage>? inspect = null,
        HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            inspect?.Invoke(request);
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            });
        }
    }
}
