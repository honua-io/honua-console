using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free coverage for the layer-relationships OPERATION (gap report Bucket 3-A #3): the
/// <see cref="HonuaServerConsoleLayerRelationshipsOperation"/> over a stubbed admin client and the
/// missing-binding <see cref="UnsupportedConsoleLayerRelationshipsOperation"/>. Asserts the real
/// route/verb/body each read+write issues (GET/PUT
/// /api/v1/admin/metadata/layers/{id}/relationships), the result mapping, and that the unconfigured surface
/// never performs a network call. No mocks of relationship data — every assertion is over the wire the
/// operation actually sends, or what a recorded server response maps to.
/// </summary>
public sealed class LayerRelationshipsOperationTests
{
    private static readonly Uri BaseAddress = new("https://honua.test");

    [Fact]
    public async Task GetRelationships_IssuesGetToRelationshipsRoute_AndMapsRows()
    {
        string? path = null;
        HttpMethod? method = null;
        var data = new HonuaAdminLayerRelationships
        {
            LayerId = 1,
            Relationships =
            [
                new HonuaAdminLayerRelationship
                {
                    Id = "rel0",
                    Name = "self",
                    RelatedLayerId = 1,
                    Role = "origin",
                    Cardinality = "one-to-many",
                    OriginField = "id",
                    DestinationField = "id",
                    EsriRelationshipId = 7,
                }
            ],
        };
        var operation = new HonuaServerConsoleLayerRelationshipsOperation(CreateClient(request =>
        {
            path = request.RequestUri?.AbsolutePath;
            method = request.Method;
            return Ok(data);
        }));

        var result = await operation.GetRelationshipsAsync(1);

        Assert.True(result.Bound);
        Assert.Equal(HttpMethod.Get, method);
        Assert.Equal("/api/v1/admin/metadata/layers/1/relationships", path);
        var row = Assert.Single(result.Relationships);
        Assert.Equal("self", row.Name);
        Assert.Equal(1, row.RelatedLayerId);
        Assert.Equal("origin", row.Role);
        Assert.Equal("one-to-many", row.Cardinality);
        Assert.Equal("id", row.OriginField);
        Assert.Equal("id", row.DestinationField);
        Assert.Equal(7, row.EsriRelationshipId);
    }

    [Fact]
    public async Task SetRelationships_IssuesPutWithRows_AndMapsUpdated()
    {
        string? path = null;
        HttpMethod? method = null;
        string? body = null;
        var operation = new HonuaServerConsoleLayerRelationshipsOperation(CreateClient(request =>
        {
            path = request.RequestUri?.AbsolutePath;
            method = request.Method;
            body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return Ok(new HonuaAdminLayerRelationships { LayerId = 1 });
        }));

        var result = await operation.SetRelationshipsAsync(1,
        [
            new ConsoleLayerRelationship
            {
                Id = "rel0",
                Name = "self",
                RelatedLayerId = 1,
                Role = "origin",
                Cardinality = "one-to-many",
                OriginField = "id",
                DestinationField = "id",
                EsriRelationshipId = 7,
            }
        ]);

        Assert.True(result.Succeeded);
        Assert.Equal("Updated", result.State);
        Assert.Equal(HttpMethod.Put, method);
        Assert.Equal("/api/v1/admin/metadata/layers/1/relationships", path);

        // Assert the PUT carried the relationship row with the live e2e body shape.
        using var doc = JsonDocument.Parse(body!);
        var rels = doc.RootElement.GetProperty("relationships");
        Assert.Equal(1, rels.GetArrayLength());
        var rel = rels[0];
        Assert.Equal("self", rel.GetProperty("name").GetString());
        Assert.Equal(1, rel.GetProperty("relatedLayerId").GetInt32());
        Assert.Equal("origin", rel.GetProperty("role").GetString());
        Assert.Equal("one-to-many", rel.GetProperty("cardinality").GetString());
        Assert.Equal("id", rel.GetProperty("originField").GetString());
        Assert.Equal("id", rel.GetProperty("destinationField").GetString());
        Assert.Equal(7, rel.GetProperty("esriRelationshipId").GetInt32());
    }

    [Fact]
    public async Task SetRelationships_Empty_IssuesPutWithEmptyArray_AndMapsCleared()
    {
        string? body = null;
        var operation = new HonuaServerConsoleLayerRelationshipsOperation(CreateClient(request =>
        {
            body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return Ok(new HonuaAdminLayerRelationships { LayerId = 1 });
        }));

        var result = await operation.SetRelationshipsAsync(1, []);

        Assert.True(result.Succeeded);
        Assert.Contains("Cleared", result.Detail!, StringComparison.Ordinal);
        Assert.Contains("\"relationships\":[]", body!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SetRelationships_WhenServerRejects_MapsFailureWithDetail()
    {
        var operation = new HonuaServerConsoleLayerRelationshipsOperation(CreateClient(_ =>
            new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = JsonContent.Create(new { success = false, message = "relatedLayerId 99 does not exist." })
            }));

        var result = await operation.SetRelationshipsAsync(1,
        [
            new ConsoleLayerRelationship { Name = "bad", RelatedLayerId = 99, Role = "origin" }
        ]);

        Assert.False(result.Succeeded);
        Assert.Contains("does not exist", result.Detail!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unsupported_NeverCallsNetwork_AndReturnsMissingBinding()
    {
        var operation = new UnsupportedConsoleLayerRelationshipsOperation();

        var read = await operation.GetRelationshipsAsync(1);
        var write = await operation.SetRelationshipsAsync(1,
        [
            new ConsoleLayerRelationship { Name = "x", RelatedLayerId = 2, Role = "origin" }
        ]);

        Assert.False(read.Bound);
        Assert.Contains("HONUA_SERVER_BASE_URL", read.Detail!, StringComparison.Ordinal);
        Assert.False(write.Succeeded);
        Assert.Equal("Missing binding", write.State);
        Assert.Contains("HONUA_SERVER_BASE_URL", write.Detail!, StringComparison.Ordinal);
    }

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
            // Materialize content before returning so the request-body assertion can read it.
            _ = request.Content?.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
            return Task.FromResult(responder(request));
        }
    }
}
