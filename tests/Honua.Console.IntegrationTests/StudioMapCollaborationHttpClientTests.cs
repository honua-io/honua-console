using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Honua.Console.Contracts;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free coverage for <see cref="HonuaStudioMapCollaborationHttpClient"/> transport + contract
/// semantics against the honua-server durable Studio map collaboration API (honua-server#1278, slice 1).
/// Asserts: the {success,data} envelope is unwrapped to the typed projection; reads/writes target the exact
/// map-scoped collab routes and carry the admin API key; the create POST serializes the anchor + body; the
/// activity GET appends the limit; 404 maps to Unsupported (contract/thread absent), 403 to Missing
/// permission, 400 to Invalid request; caller cancellation propagates while an HttpClient timeout is
/// Unavailable.
/// </summary>
public sealed class StudioMapCollaborationHttpClientTests
{
    private static readonly Uri BaseAddress = new("https://honua.test");
    private const string ApiKey = "admin-key-xyz";

    [Fact]
    public async Task ListThreads_OnSuccess_UnwrapsEnvelopeAndTargetsCollabRouteWithApiKey()
    {
        string? path = null;
        string? apiKey = null;
        var list = new HonuaStudioMapCommentThreadList
        {
            MapId = "map-1",
            Threads =
            [
                new HonuaStudioMapCommentThread
                {
                    ThreadId = "thread-1",
                    FeatureLabel = "Parcel 04-021-204",
                    LayerRef = "parcels/fill",
                    CommentCount = 2,
                    Resolved = false,
                    XFraction = 0.37,
                    YFraction = 0.52,
                    Messages =
                    [
                        new HonuaStudioMapCommentMessage
                        {
                            MessageId = "m1",
                            AuthorName = "a.lee",
                            AuthorInitials = "AL",
                            AuthorColor = "#1d6b3e",
                            RelativeTime = "14m ago",
                            Body = "reclassified to R-2"
                        }
                    ]
                }
            ]
        };
        using var client = CreateClient(new RecordingHandler(request =>
        {
            path = request.RequestUri?.AbsolutePath;
            apiKey = request.Headers.TryGetValues("X-API-Key", out var v) ? v.FirstOrDefault() : null;
            return Ok(list);
        }));

        var result = await client.ListThreadsAsync("map-1");

        Assert.Null(result.Issue);
        Assert.Equal("/api/v1/console/maps/map-1/collab/comments", path);
        Assert.Equal(ApiKey, apiKey);
        var thread = Assert.Single(result.Data!.Threads);
        Assert.Equal("thread-1", thread.ThreadId);
        Assert.Equal("Parcel 04-021-204", thread.FeatureLabel);
        Assert.Equal(2, thread.CommentCount);
        Assert.Single(thread.Messages);
    }

    [Fact]
    public async Task ListActivity_AppendsLimitQueryAndTargetsActivityRoute()
    {
        string? path = null;
        string? query = null;
        using var client = CreateClient(new RecordingHandler(request =>
        {
            path = request.RequestUri?.AbsolutePath;
            query = request.RequestUri?.Query;
            return Ok(new HonuaStudioMapActivityList { MapId = "map-1", Activity = [] });
        }));

        var result = await client.ListActivityAsync("map-1", limit: 25);

        Assert.Null(result.Issue);
        Assert.Equal("/api/v1/console/maps/map-1/collab/activity", path);
        Assert.Equal("?limit=25", query);
    }

    [Fact]
    public async Task CreateThread_SerializesAnchorAndBodyAndPostsToCommentsRoute()
    {
        string? path = null;
        HttpMethod? method = null;
        JsonElement body = default;
        using var client = CreateClient(new RecordingHandler(request =>
        {
            path = request.RequestUri?.AbsolutePath;
            method = request.Method;
            body = ReadBody(request);
            return Ok(new HonuaStudioMapCommentThread
            {
                ThreadId = "thread-1",
                FeatureLabel = "Parcel 04-021-204",
                LayerRef = "parcels/fill",
                CommentCount = 1,
                XFraction = 0.37,
                YFraction = 0.52,
                Messages = []
            });
        }));

        var result = await client.CreateThreadAsync("map-1", new HonuaCreateStudioMapCommentThreadRequest
        {
            FeatureLabel = "Parcel 04-021-204",
            LayerRef = "parcels/fill",
            XFraction = 0.37,
            YFraction = 0.52,
            Body = "reclassified to R-2"
        });

        Assert.Null(result.Issue);
        Assert.Equal(HttpMethod.Post, method);
        Assert.Equal("/api/v1/console/maps/map-1/collab/comments", path);
        Assert.Equal("Parcel 04-021-204", body.GetProperty("featureLabel").GetString());
        Assert.Equal("parcels/fill", body.GetProperty("layerRef").GetString());
        Assert.Equal(0.37, body.GetProperty("xFraction").GetDouble(), 3);
        Assert.Equal("reclassified to R-2", body.GetProperty("body").GetString());
    }

    [Fact]
    public async Task AddReply_PostsBodyToRepliesRoute()
    {
        string? path = null;
        JsonElement body = default;
        using var client = CreateClient(new RecordingHandler(request =>
        {
            path = request.RequestUri?.AbsolutePath;
            body = ReadBody(request);
            return Ok(new HonuaStudioMapCommentThread
            {
                ThreadId = "thread-1",
                FeatureLabel = "f",
                LayerRef = "l",
                CommentCount = 2,
                Messages = []
            });
        }));

        var result = await client.AddReplyAsync("map-1", "thread-1", new HonuaCreateStudioMapCommentReplyRequest { Body = "confirmed" });

        Assert.Null(result.Issue);
        Assert.Equal("/api/v1/console/maps/map-1/collab/comments/thread-1/replies", path);
        Assert.Equal("confirmed", body.GetProperty("body").GetString());
    }

    [Fact]
    public async Task SetResolved_PostsResolvedFlagToResolveRoute()
    {
        string? path = null;
        JsonElement body = default;
        using var client = CreateClient(new RecordingHandler(request =>
        {
            path = request.RequestUri?.AbsolutePath;
            body = ReadBody(request);
            return Ok(new HonuaStudioMapCommentThread
            {
                ThreadId = "thread-1",
                FeatureLabel = "f",
                LayerRef = "l",
                Resolved = true,
                Messages = []
            });
        }));

        var result = await client.SetResolvedAsync("map-1", "thread-1", new HonuaResolveStudioMapCommentThreadRequest { Resolved = true });

        Assert.Null(result.Issue);
        Assert.Equal("/api/v1/console/maps/map-1/collab/comments/thread-1/resolve", path);
        Assert.True(body.GetProperty("resolved").GetBoolean());
        Assert.True(result.Data!.Resolved);
    }

    [Fact]
    public async Task ListThreads_WhenNotFound_MapsToUnsupported()
    {
        using var client = CreateClient(new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));

        var result = await client.ListThreadsAsync("map-1");

        Assert.NotNull(result.Issue);
        Assert.Equal("Unsupported", result.Issue!.State);
        Assert.Equal(404, result.Issue.StatusCode);
    }

    [Fact]
    public async Task ListThreads_WhenForbidden_MapsToMissingPermission()
    {
        using var client = CreateClient(new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)));

        var result = await client.ListThreadsAsync("map-1");

        Assert.NotNull(result.Issue);
        Assert.Equal("Missing permission", result.Issue!.State);
        Assert.Equal(403, result.Issue.StatusCode);
    }

    [Fact]
    public async Task CreateThread_WhenBadRequest_MapsToInvalidRequest()
    {
        using var client = CreateClient(new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)));

        var result = await client.CreateThreadAsync("map-1", new HonuaCreateStudioMapCommentThreadRequest
        {
            FeatureLabel = "f",
            LayerRef = "l",
            XFraction = 0.1,
            YFraction = 0.1,
            Body = ""
        });

        Assert.NotNull(result.Issue);
        Assert.Equal("Invalid request", result.Issue!.State);
        Assert.Equal(400, result.Issue.StatusCode);
    }

    [Fact]
    public async Task ListThreads_WhenCallerCancels_PropagatesCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        using var client = CreateClient(new BlockUntilCancelledHandler());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.ListThreadsAsync("map-1", cts.Token));
    }

    [Fact]
    public async Task ListThreads_OnHttpClientTimeout_ReturnsUnavailable()
    {
        using var client = CreateClient(new BlockUntilCancelledHandler(), TimeSpan.FromMilliseconds(50));

        var result = await client.ListThreadsAsync("map-1", CancellationToken.None);

        Assert.NotNull(result.Issue);
        Assert.Equal("Unavailable", result.Issue!.State);
    }

    private static HttpResponseMessage Ok<T>(T data) =>
        new(HttpStatusCode.OK) { Content = JsonContent.Create(new { success = true, data, message = (string?)null, timestamp = DateTimeOffset.UtcNow }) };

    private static JsonElement ReadBody(HttpRequestMessage request)
    {
        var json = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    private static HonuaStudioMapCollaborationHttpClient CreateClient(HttpMessageHandler handler, TimeSpan? timeout = null)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = BaseAddress };
        if (timeout is { } value)
        {
            httpClient.Timeout = value;
        }

        return new HonuaStudioMapCollaborationHttpClient(httpClient, new HonuaStudioMapCollaborationClientOptions(BaseAddress, ApiKey));
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _factory;

        public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> factory) => _factory = factory;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content is not null)
            {
                _ = request.Content.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
            }

            return Task.FromResult(_factory(request));
        }
    }

    private sealed class BlockUntilCancelledHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
