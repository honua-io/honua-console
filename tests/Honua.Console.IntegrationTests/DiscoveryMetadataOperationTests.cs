using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free coverage for the discovery-metadata authoring OPERATION (gapb/discovery): the
/// <see cref="HonuaServerConsoleDiscoveryMetadataOperation"/> over a stubbed admin client and the
/// missing-binding <see cref="UnsupportedConsoleDiscoveryMetadataOperation"/>. Asserts the real route/verb/
/// body each read+write issues for BOTH a layer
/// (<c>GET/PUT /api/v1/admin/metadata/layers/{id}/discovery</c>) and a service
/// (<c>GET/PUT /api/v1/admin/services/{svc}/discovery</c>) — including the keywords/themes/links arrays and
/// the nested contactPoint — the result mapping, and that the unconfigured surface never performs a network
/// call. No mocks of discovery data: every assertion is over the wire the operation actually sends, or what a
/// recorded server response maps to.
/// </summary>
public sealed class DiscoveryMetadataOperationTests
{
    private static readonly Uri BaseAddress = new("https://honua.test");

    [Fact]
    public async Task GetLayerDiscovery_IssuesGetToLayerDiscoveryRoute_AndMapsFields()
    {
        string? path = null;
        HttpMethod? method = null;
        var data = SampleMetadata();
        var operation = new HonuaServerConsoleDiscoveryMetadataOperation(CreateClient(request =>
        {
            path = request.RequestUri?.AbsolutePath;
            method = request.Method;
            return Ok(data);
        }));

        var result = await operation.GetLayerDiscoveryAsync(42);

        Assert.True(result.Bound);
        Assert.Equal(HttpMethod.Get, method);
        Assert.Equal("/api/v1/admin/metadata/layers/42/discovery", path);
        Assert.Equal("Parcels", result.Title);
        Assert.Equal(new[] { "cadastre", "parcels" }, result.Keywords);
        Assert.Equal(new[] { "boundaries" }, result.Themes);
        Assert.Equal("CC-BY-4.0", result.License);
        Assert.Equal("County GIS", result.Attribution);
        Assert.Equal("County GIS Department", result.Publisher);
        Assert.Equal("GIS Help Desk", result.ContactPoint?.Name);
        Assert.Equal("gis@county.example", result.ContactPoint?.Email);
        var link = Assert.Single(result.Links);
        Assert.Equal("https://county.example/metadata", link.Href);
        Assert.Equal("describedby", link.Rel);
    }

    [Fact]
    public async Task SaveLayerDiscovery_IssuesPutWithArraysAndContact_ToLayerRoute()
    {
        string? path = null;
        HttpMethod? method = null;
        string? body = null;
        var operation = new HonuaServerConsoleDiscoveryMetadataOperation(CreateClient(request =>
        {
            path = request.RequestUri?.AbsolutePath;
            method = request.Method;
            body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return Ok(SampleMetadata());
        }));

        var result = await operation.SaveLayerDiscoveryAsync(42, new ConsoleDiscoveryMetadata
        {
            Bound = true,
            Title = "Parcels",
            Description = "Cadastral parcels.",
            Keywords = new[] { "cadastre", "parcels" },
            Themes = new[] { "boundaries" },
            Language = "en",
            License = "CC-BY-4.0",
            Attribution = "County GIS",
            Publisher = "County GIS Department",
            ContactPoint = new ConsoleDiscoveryContactPoint { Name = "GIS Help Desk", Email = "gis@county.example", Url = "https://county.example/gis" },
            Links = new[] { new ConsoleDiscoveryLink { Href = "https://county.example/metadata", Rel = "describedby", Type = "text/html", Title = "Metadata", Hreflang = "en" } },
        });

        Assert.True(result.Succeeded);
        Assert.Equal("Updated", result.State);
        Assert.Equal(HttpMethod.Put, method);
        Assert.Equal("/api/v1/admin/metadata/layers/42/discovery", path);

        using var doc = JsonDocument.Parse(body!);
        var root = doc.RootElement;
        Assert.Equal("Parcels", root.GetProperty("title").GetString());
        Assert.Equal("CC-BY-4.0", root.GetProperty("license").GetString());
        var keywords = root.GetProperty("keywords");
        Assert.Equal(2, keywords.GetArrayLength());
        Assert.Equal("cadastre", keywords[0].GetString());
        Assert.Equal("boundaries", root.GetProperty("themes")[0].GetString());
        Assert.Equal("GIS Help Desk", root.GetProperty("contactPoint").GetProperty("name").GetString());
        var links = root.GetProperty("links");
        Assert.Equal(1, links.GetArrayLength());
        Assert.Equal("https://county.example/metadata", links[0].GetProperty("href").GetString());
        Assert.Equal("describedby", links[0].GetProperty("rel").GetString());
    }

    [Fact]
    public async Task GetServiceDiscovery_IssuesGetToServiceDiscoveryRoute()
    {
        string? path = null;
        HttpMethod? method = null;
        var operation = new HonuaServerConsoleDiscoveryMetadataOperation(CreateClient(request =>
        {
            path = request.RequestUri?.AbsolutePath;
            method = request.Method;
            return Ok(SampleMetadata());
        }));

        var result = await operation.GetServiceDiscoveryAsync("parcels");

        Assert.True(result.Bound);
        Assert.Equal(HttpMethod.Get, method);
        Assert.Equal("/api/v1/admin/services/parcels/discovery", path);
    }

    [Fact]
    public async Task SaveServiceDiscovery_IssuesPutToServiceRoute_WithEmptyListsClearingLists()
    {
        string? path = null;
        string? body = null;
        var operation = new HonuaServerConsoleDiscoveryMetadataOperation(CreateClient(request =>
        {
            path = request.RequestUri?.AbsolutePath;
            body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return Ok(new HonuaAdminDiscoveryMetadata { Title = "svc" });
        }));

        var result = await operation.SaveServiceDiscoveryAsync("parcels", new ConsoleDiscoveryMetadata
        {
            Bound = true,
            Title = "Parcels service",
            Keywords = Array.Empty<string>(),
            Themes = Array.Empty<string>(),
            Links = Array.Empty<ConsoleDiscoveryLink>(),
        });

        Assert.True(result.Succeeded);
        Assert.Equal("/api/v1/admin/services/parcels/discovery", path);
        // Empty form lists serialize to [] so the server clears those lists.
        Assert.Contains("\"keywords\":[]", body!, StringComparison.Ordinal);
        Assert.Contains("\"themes\":[]", body!, StringComparison.Ordinal);
        Assert.Contains("\"links\":[]", body!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaveDiscovery_WhenServerRejects_MapsFailureWithDetail()
    {
        var operation = new HonuaServerConsoleDiscoveryMetadataOperation(CreateClient(_ =>
            new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = JsonContent.Create(new { success = false, message = "license 'BOGUS' is not a known SPDX id." })
            }));

        var result = await operation.SaveLayerDiscoveryAsync(42, new ConsoleDiscoveryMetadata { Bound = true, License = "BOGUS" });

        Assert.False(result.Succeeded);
        Assert.Contains("not a known SPDX", result.Detail!, StringComparison.Ordinal);
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
