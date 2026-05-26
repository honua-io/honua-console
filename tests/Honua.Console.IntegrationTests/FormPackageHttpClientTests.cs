using System.Net;
using Honua.Console.Contracts;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free coverage for <see cref="HonuaFormPackageHttpClient"/> transport semantics. Caller-requested
/// cancellation must propagate (it cancels the calling operation rather than masquerading as an Unavailable
/// endpoint issue the caller would mistake for a server failure), while an HttpClient timeout still surfaces
/// as Unavailable. Mirrors the cancellation contract the adjacent Studio package lifecycle client guarantees.
/// </summary>
public sealed class FormPackageHttpClientTests
{
    private static readonly Uri BaseAddress = new("https://honua.test");

    [Fact]
    public async Task ListPackages_WhenCallerCancels_PropagatesCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        using var client = CreateClient(new BlockUntilCancelledHandler());

        // The caller's token is already cancelled, so the call must throw rather than swallow the
        // cancellation into an Unavailable result the caller would read as a transport failure.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.ListPackagesAsync(cts.Token));
    }

    [Fact]
    public async Task ListPackages_OnHttpClientTimeout_ReturnsUnavailable()
    {
        using var client = CreateClient(new BlockUntilCancelledHandler(), TimeSpan.FromMilliseconds(50));

        // The HttpClient timeout fires (not the caller's token, which is None), which is a transport failure,
        // so it must map to Unavailable instead of propagating as a cancellation the caller never requested.
        var result = await client.ListPackagesAsync(CancellationToken.None);

        Assert.NotNull(result.Issue);
        Assert.Equal("Unavailable", result.Issue!.State);
    }

    private static HonuaFormPackageHttpClient CreateClient(HttpMessageHandler handler, TimeSpan? timeout = null)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = BaseAddress };
        if (timeout is { } value)
        {
            httpClient.Timeout = value;
        }

        return new HonuaFormPackageHttpClient(httpClient, new HonuaFormPackageClientOptions(BaseAddress));
    }

    private sealed class BlockUntilCancelledHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // Never completes on its own: the request ends only when the linked token (caller cancel or the
            // HttpClient timeout) fires, exactly like a real in-flight request being cancelled.
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
