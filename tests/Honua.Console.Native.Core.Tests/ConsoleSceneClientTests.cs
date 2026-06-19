using System.Net;
using System.Text;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.Native.Core.Tests;

public sealed class ConsoleSceneClientTests
{
    [Fact]
    public async Task ListScenes_ReadsPublicSceneDiscoveryEndpoint()
    {
        var handler = new RecordingHandler(request => request.RequestUri!.AbsolutePath == "/api/scenes"
            ? Json(
                """
                {
                  "scenes": [
                    {
                      "id": "demo-pointcloud",
                      "name": "Demo point cloud",
                      "description": "Synthetic LAS-derived tileset.",
                      "tilesetUrl": "https://server.example/scenes/demo-pointcloud/tileset.json",
                      "bounds": { "west": -122.5, "south": 37.7, "east": -122.4, "north": 37.8 },
                      "capabilities": ["3dtiles", "pointcloud"]
                    }
                  ]
                }
                """)
            : new HttpResponseMessage(HttpStatusCode.NotFound));
        var client = CreateClient(handler);

        var result = await client.ListScenesAsync();

        Assert.True(result.IsOk);
        var scene = Assert.Single(result.Value!.Scenes);
        Assert.Equal("demo-pointcloud", scene.Id);
        Assert.Contains("pointcloud", scene.Capabilities);
    }

    [Fact]
    public async Task IngestPointCloud_PostsMultipartToAdminEndpointAndReturnsTileset()
    {
        HttpRequestMessage? captured = null;
        var handler = new RecordingHandler(request =>
        {
            captured = request;
            if (request.Method == HttpMethod.Post
                && request.RequestUri!.AbsolutePath == "/api/v1/admin/scenes/ingest/pointcloud")
            {
                return new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = new StringContent(
                        """
                        {
                          "sceneId": "pc-abc123",
                          "tilesetUrl": "https://server.example/scenes/pc-abc123/tileset.json",
                          "pointCount": 50000,
                          "tileCount": 8,
                          "hasColor": true,
                          "boundingRegionDegrees": [-122.5, 37.7, -122.4, 37.8]
                        }
                        """,
                        Encoding.UTF8,
                        "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var client = CreateClient(handler, adminApiKey: "admin-key");

        var result = await client.IngestPointCloudAsync(new PointCloudIngestRequest
        {
            Content = Encoding.ASCII.GetBytes("LASF-fake-point-cloud"),
            FileName = "survey.las",
            DisplayName = "Survey"
        });

        Assert.True(result.IsOk);
        Assert.Equal("pc-abc123", result.Value!.SceneId);
        Assert.Equal(50000, result.Value.PointCount);
        Assert.True(result.Value.HasColor);
        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Post, captured!.Method);
        // multipart/form-data carrying the LAS file field.
        Assert.Contains("multipart/form-data", captured.Content!.Headers.ContentType!.MediaType, StringComparison.Ordinal);
        // admin API key forwarded for the admin-authorized ingest endpoint.
        Assert.True(captured.Headers.Contains("X-API-Key"));
    }

    [Fact]
    public async Task IngestPointCloud_MapsPaymentRequiredToEnterpriseGate()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.PaymentRequired));
        var client = CreateClient(handler);

        var result = await client.IngestPointCloudAsync(new PointCloudIngestRequest
        {
            Content = [1, 2, 3],
            FileName = "survey.las"
        });

        Assert.False(result.IsOk);
        Assert.Equal(SceneReadStatus.PaymentRequired, result.Status);
    }

    [Fact]
    public async Task ResolveTilesetUrl_BuildsSceneServeRoute()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = CreateClient(handler);

        var url = await client.ResolveTilesetUrlAsync("pc-abc123");

        Assert.Equal("https://server.example/scenes/pc-abc123/tileset.json", url);
    }

    [Fact]
    public async Task ListScenes_WhenNoProfile_ReturnsMissingBinding()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var profiles = new InMemoryConsoleEnvironmentProfileStore([], activeProfileId: null);
        var client = new HttpConsoleSceneClient(
            new HttpClient(handler), profiles, new InMemoryConsoleAccountSessionStore());

        var result = await client.ListScenesAsync();

        Assert.False(result.IsOk);
        Assert.Equal(SceneReadStatus.Unavailable, result.Status);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Unsupported_DiscoverAndIngestRenderMissingBinding()
    {
        var client = new UnsupportedConsoleSceneClient();

        var scenes = await client.ListScenesAsync();
        var ingest = await client.IngestPointCloudAsync(new PointCloudIngestRequest { Content = [1], FileName = "a.las" });
        var url = await client.ResolveTilesetUrlAsync("x");

        Assert.Equal(SceneReadStatus.Unavailable, scenes.Status);
        Assert.Equal(SceneReadStatus.Unavailable, ingest.Status);
        Assert.Null(url);
    }

    [Fact]
    public async Task InMemory_IngestAddsDiscoverableScene()
    {
        var client = new InMemoryConsoleSceneClient(scenes: []);

        var ingest = await client.IngestPointCloudAsync(new PointCloudIngestRequest
        {
            Content = Encoding.ASCII.GetBytes("LASF-fake"),
            FileName = "a.las",
            DisplayName = "A"
        });
        var scenes = await client.ListScenesAsync();

        Assert.True(ingest.IsOk);
        Assert.Contains(scenes.Value!.Scenes, s => s.Id == ingest.Value!.SceneId);
    }

    private static HttpConsoleSceneClient CreateClient(HttpMessageHandler handler, string? adminApiKey = null)
    {
        var profile = new ConsoleEnvironmentProfile
        {
            Id = "live",
            DisplayName = "Live Server",
            ServerBaseUri = new Uri("https://server.example"),
            UpdatedAt = DateTimeOffset.Parse("2026-06-18T19:00:00Z"),
            Account = new ConsoleAccountBinding
            {
                AuthMode = ConsoleAccountAuthMode.AccountRbac,
                AccountId = "operator.live"
            }
        };
        var profiles = new InMemoryConsoleEnvironmentProfileStore([profile], activeProfileId: profile.Id);
        return new HttpConsoleSceneClient(
            new HttpClient(handler), profiles, new InMemoryConsoleAccountSessionStore(), adminApiKey);
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(CloneRequest(request));
            return Task.FromResult(responder(request));
        }

        private static HttpRequestMessage CloneRequest(HttpRequestMessage request)
        {
            var clone = new HttpRequestMessage(request.Method, request.RequestUri);
            foreach (var header in request.Headers)
            {
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            return clone;
        }
    }
}
