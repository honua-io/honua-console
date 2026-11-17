using System.Net;
using Honua.Console.Contracts;

namespace Honua.Console.IntegrationTests;

public sealed class WorkflowBodyFailureTests
{
    public enum Failure { Connection, Timeout, Io }

    [Theory]
    [InlineData(HttpStatusCode.OK, Failure.Connection)]
    [InlineData(HttpStatusCode.OK, Failure.Timeout)]
    [InlineData(HttpStatusCode.OK, Failure.Io)]
    [InlineData(HttpStatusCode.BadRequest, Failure.Connection)]
    [InlineData(HttpStatusCode.BadRequest, Failure.Timeout)]
    [InlineData(HttpStatusCode.BadRequest, Failure.Io)]
    [InlineData(HttpStatusCode.Forbidden, Failure.Io)]
    [InlineData(HttpStatusCode.Conflict, Failure.Io)]
    public async Task InterruptedBodyProducesIssueAndDisposesResponse(HttpStatusCode status, Failure failure)
    {
        var content = new InterruptedContent(() => throw Error(failure));
        using var client = Client(status, content);

        var result = await client.GetNodeRegistryAsync();

        Assert.False(result.IsSuccess);
        Assert.Null(result.Data);
        Assert.Equal(status == HttpStatusCode.OK ? "Unavailable" : status switch
        {
            HttpStatusCode.BadRequest => "Validation failed",
            HttpStatusCode.Forbidden => "Missing permission",
            HttpStatusCode.Conflict => "Conflict",
            _ => throw new InvalidOperationException()
        }, result.Issue!.State);
        Assert.Equal((int)status, result.Issue.StatusCode);
        Assert.True(content.Disposed);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task CallerCancellationStillCancelsAndDisposesResponse(HttpStatusCode status)
    {
        using var cancellation = new CancellationTokenSource();
        var content = new InterruptedContent(() =>
        {
            cancellation.Cancel();
            throw new TaskCanceledException("caller canceled", null, cancellation.Token);
        });
        using var client = Client(status, content);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetNodeRegistryAsync(cancellation.Token));

        Assert.True(content.Disposed);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, "Unavailable")]
    [InlineData(HttpStatusCode.BadRequest, "Validation failed")]
    public async Task InterruptedMutationIsNotReplayed(HttpStatusCode status, string state)
    {
        var content = new InterruptedContent(() => throw new IOException("stream interrupted"));
        var handler = new Handler(status, content);
        using var client = new HttpWorkflowPackageApiClient(new HttpClient(handler), new(new Uri("https://honua.test")));

        var result = await client.CreateVersionAsync("draft");

        Assert.False(result.IsSuccess);
        Assert.Equal(state, result.Issue!.State);
        Assert.Equal((int)status, result.Issue.StatusCode);
        Assert.Equal(1, handler.Calls);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.EndsWith("/workflow-packages/draft/versions", handler.Path);
        Assert.True(content.Disposed);
    }

    private static Exception Error(Failure failure) => failure switch
    {
        Failure.Connection => new HttpRequestException("connection interrupted"),
        Failure.Timeout => new TaskCanceledException("response timeout"),
        Failure.Io => new IOException("stream interrupted"),
        _ => throw new ArgumentOutOfRangeException(nameof(failure))
    };

    private static HttpWorkflowPackageApiClient Client(HttpStatusCode status, HttpContent content)
    {
        var address = new Uri("https://honua.test");
        return new HttpWorkflowPackageApiClient(new HttpClient(new Handler(status, content)), new(address));
    }

    private sealed class Handler(HttpStatusCode status, HttpContent content) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public HttpMethod? Method { get; private set; }
        public string? Path { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Method = request.Method;
            Path = request.RequestUri!.AbsolutePath;
            return Task.FromResult(new HttpResponseMessage(status) { Content = content });
        }
    }

    private sealed class InterruptedContent(Action read) : HttpContent
    {
        public bool Disposed { get; private set; }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            read();
            return Task.CompletedTask;
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}
