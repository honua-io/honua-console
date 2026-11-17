using System.Net;
using Honua.Console.Contracts;
using Honua.Console.Shell.Services;

namespace Honua.Console.Shell.Tests;

public sealed class MapReadFailureBoundaryTests
{
    [Theory]
    [InlineData(MapVariant.Saved, "connection")]
    [InlineData(MapVariant.Saved, "timeout")]
    [InlineData(MapVariant.Draft, "connection")]
    [InlineData(MapVariant.Draft, "timeout")]
    [InlineData(MapVariant.Saved, "io")]
    [InlineData(MapVariant.Draft, "io")]
    public async Task InterruptedBodyReturnsUnavailable(MapVariant variant, string failure)
    {
        using var http = new HttpClient(new BodyHandler(() => failure switch
        {
            "timeout" => new TaskCanceledException("Body read timed out"),
            "io" => new IOException("Response interrupted"),
            _ => new HttpRequestException("Response disconnected")
        }));
        using var client = new HonuaConsoleContentHttpClient(http,
            new HonuaConsoleContentClientOptions(new Uri("https://server.test")));
        var catalog = new HonuaServerConsoleCatalogClient(client);

        var result = variant == MapVariant.Draft
            ? await catalog.GetDraftMapAsync("item-1", CatalogReadContext.Authenticated)
            : await catalog.GetMapAsync("item-1", CatalogReadContext.Authenticated);

        Assert.Equal(CatalogReadStatus.Unavailable, result.Status);
    }

    [Theory]
    [InlineData(MapVariant.Saved)]
    [InlineData(MapVariant.Draft)]
    public async Task CallerCancellationDuringBodyStillPropagates(MapVariant variant)
    {
        using var cancellation = new CancellationTokenSource();
        using var http = new HttpClient(new BodyHandler(() =>
        {
            cancellation.Cancel();
            return new TaskCanceledException("Read cancelled", null, cancellation.Token);
        }));
        using var client = new HonuaConsoleContentHttpClient(http,
            new HonuaConsoleContentClientOptions(new Uri("https://server.test")));
        var catalog = new HonuaServerConsoleCatalogClient(client);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => variant == MapVariant.Draft
            ? catalog.GetDraftMapAsync("item-1", CatalogReadContext.Authenticated, cancellation.Token)
            : catalog.GetMapAsync("item-1", CatalogReadContext.Authenticated, cancellation.Token));
    }

    public enum MapVariant { Saved, Draft }

    private sealed class BodyHandler(Func<Exception> failure) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new InterruptedContent(failure) });
    }

    private sealed class InterruptedContent(Func<Exception> failure) : HttpContent
    {
        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromException<Stream>(
            failure());

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            throw new InvalidOperationException("The response must be streamed.");

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
