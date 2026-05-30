using System.Net;
using System.Text;
using System.Text.Json;
using Honua.Console.Contracts;

namespace Honua.Console.IntegrationTests;

public sealed class StudioPackageLifecycleClientTests
{
    [Fact]
    public async Task ReopenVersion_PostsToReopenRouteAndParsesDraft()
    {
        var draftId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        var handler = new RecordingHandler(request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal(
                $"/api/v1/studio/content-items/{itemId}/versions/{versionId}/reopen",
                request.RequestUri!.AbsolutePath);
            var payload = JsonSerializer.Serialize(new
            {
                success = true,
                data = new { draftId, itemId, packageKey = "studio-dashboard-ops", family = "dashboard", generation = 1 }
            });
            return Json(HttpStatusCode.Created, payload);
        });
        using var client = CreateClient(handler);

        var result = await client.ReopenVersionAsync(itemId, versionId);

        Assert.True(result.IsSuccess);
        Assert.Equal(draftId, result.Data!.DraftId);
        Assert.Equal(StudioPackageFamily.Dashboard, result.Data.Family);
        // The admin API key is carried on the request.
        Assert.Equal("test-admin-key", handler.LastApiKey);
    }

    [Fact]
    public async Task Rollback_PostsTargetVersionAndPointer()
    {
        var itemId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        string? capturedBody = null;
        var handler = new RecordingHandler(request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal($"/api/v1/studio/content-items/{itemId}/rollback-requests", request.RequestUri!.AbsolutePath);
            capturedBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            var payload = JsonSerializer.Serialize(new
            {
                success = true,
                data = new
                {
                    requestId = Guid.NewGuid(),
                    itemId,
                    targetVersionId = versionId,
                    pointer = "published",
                    pointers = new { itemId },
                    createdAt = DateTimeOffset.UtcNow
                }
            });
            return Json(HttpStatusCode.Created, payload);
        });
        using var client = CreateClient(handler);

        var result = await client.RollbackAsync(
            itemId,
            new CreateStudioRollbackRequest { TargetVersionId = versionId, Target = StudioRollbackPointer.Published });

        Assert.True(result.IsSuccess);
        Assert.Equal(versionId, result.Data!.TargetVersionId);
        Assert.Equal(StudioRollbackPointer.Published, result.Data.Target);
        Assert.NotNull(capturedBody);
        // The pointer enum serializes to its wire member name, not the .NET name.
        Assert.Contains("\"pointer\":\"published\"", capturedBody, StringComparison.Ordinal);
        Assert.Contains(versionId.ToString(), capturedBody!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Reopen_NotFound_MapsToUnsupportedIssue()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        using var client = CreateClient(handler);

        var result = await client.ReopenVersionAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.False(result.IsSuccess);
        Assert.Equal("Unsupported", result.Issue!.State);
        Assert.Equal((int)HttpStatusCode.NotFound, result.Issue.StatusCode);
    }

    [Fact]
    public async Task Rollback_Conflict_MapsToConflictIssue()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Conflict));
        using var client = CreateClient(handler);

        var result = await client.RollbackAsync(
            Guid.NewGuid(),
            new CreateStudioRollbackRequest { TargetVersionId = Guid.NewGuid() });

        Assert.False(result.IsSuccess);
        Assert.Equal("Conflict", result.Issue!.State);
        Assert.True(result.Issue.IsConflict);
    }

    [Fact]
    public async Task CallerCancellationPropagatesInsteadOfEndpointIssue()
    {
        using var cancellation = new CancellationTokenSource();
        using var client = CreateClient(new ThrowingHandler(_ => new TaskCanceledException("caller canceled")));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.ListPackageFamiliesAsync(cancellation.Token));
    }

    [Fact]
    public async Task TransportTimeoutMapsToUnavailableIssue()
    {
        using var client = CreateClient(new ThrowingHandler(_ => new TaskCanceledException("timeout")));

        var result = await client.ListPackageFamiliesAsync();

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Issue);
        Assert.Equal("Unavailable", result.Issue.State);
        Assert.Contains("could not be reached", result.Issue.Detail, StringComparison.Ordinal);
    }

    private static HttpStudioPackageLifecycleClient CreateClient(HttpMessageHandler handler) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("https://honua.test") },
            new StudioPackageLifecycleClientOptions(new Uri("https://honua.test"), "test-admin-key"));

    private static HttpResponseMessage Json(HttpStatusCode statusCode, string body) =>
        new(statusCode) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        public string? LastApiKey { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastApiKey = request.Headers.TryGetValues("X-API-Key", out var values)
                ? values.FirstOrDefault()
                : null;
            return Task.FromResult(_responder(request));
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        private readonly Func<CancellationToken, Exception> _exceptionFactory;

        public ThrowingHandler(Func<CancellationToken, Exception> exceptionFactory)
        {
            _exceptionFactory = exceptionFactory;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            throw _exceptionFactory(cancellationToken);
        }
    }
}
