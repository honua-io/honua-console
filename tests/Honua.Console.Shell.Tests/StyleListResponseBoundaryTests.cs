using System.Net;
using System.Text;
using Honua.Console.Contracts;

namespace Honua.Console.Shell.Tests;

public sealed class StyleListResponseBoundaryTests
{
    [Theory]
    [InlineData("connection")]
    [InlineData("timeout")]
    [InlineData("io")]
    public async Task InterruptedListBodyReturnsUnavailableAndCanRetry(string failure)
    {
        using var handler = new InterruptedListHandler(failure);
        using var http = new HttpClient(handler);
        using var client = new HonuaOgcStylesHttpClient(http,
            new HonuaOgcStylesClientOptions(new Uri("https://server.test")));

        var interrupted = await client.ListStylesAsync();

        Assert.Null(interrupted.Data);
        Assert.Equal("Unavailable", interrupted.Issue?.State);
        Assert.True(handler.InterruptedContent.IsDisposed);

        var retry = await client.ListStylesAsync();
        Assert.Null(retry.Issue);
        Assert.Equal("night", Assert.Single(retry.Data!.Styles).Id);
    }

    [Fact]
    public async Task CallerCancellationDuringListBodyStillPropagates()
    {
        using var cancellation = new CancellationTokenSource();
        using var handler = new InterruptedListHandler("timeout", cancellation);
        using var http = new HttpClient(handler);
        using var client = new HonuaOgcStylesHttpClient(http,
            new HonuaOgcStylesClientOptions(new Uri("https://server.test")));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.ListStylesAsync(cancellation.Token));

        Assert.True(handler.InterruptedContent.IsDisposed);
    }

    private sealed class InterruptedListHandler(string failure, CancellationTokenSource? callerCancellation = null) : HttpMessageHandler
    {
        private bool _interrupted;
        public InterruptedContent InterruptedContent { get; } = new(failure, callerCancellation);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            HttpContent content = _interrupted
                ? new StringContent("{\"styles\":[{\"id\":\"night\"}]}", Encoding.UTF8, "application/json")
                : InterruptedContent;
            _interrupted = true;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }

    private sealed class InterruptedContent(string failure, CancellationTokenSource? callerCancellation) : HttpContent
    {
        public bool IsDisposed { get; private set; }

        protected override Task<Stream> CreateContentReadStreamAsync()
        {
            callerCancellation?.Cancel();
            return Task.FromException<Stream>(failure switch
            {
                "timeout" => new TaskCanceledException("Body read timed out"),
                "io" => new IOException("Response interrupted"),
                _ => new HttpRequestException("Response disconnected")
            });
        }

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            throw new InvalidOperationException("The response must be streamed.");

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
