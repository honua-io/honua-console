using System.Net;
using Honua.Console.Contracts;

namespace Honua.Console.Shell.Tests;

public sealed class StyleSaveCancellationTests
{
    public enum SaveMode { Ordinary, Conditional, Bound }

    [Theory]
    [InlineData(SaveMode.Ordinary)]
    [InlineData(SaveMode.Conditional)]
    [InlineData(SaveMode.Bound)]
    public async Task CallerCancellationPropagatesWithoutReplayingSave(SaveMode mode)
    {
        using var cancellation = new CancellationTokenSource();
        using var handler = new SaveHandler(() =>
        {
            cancellation.Cancel();
            throw new TaskCanceledException("caller canceled", null, cancellation.Token);
        });
        using var client = Client(handler);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Save(client, mode, cancellation.Token));

        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData(SaveMode.Ordinary)]
    [InlineData(SaveMode.Conditional)]
    [InlineData(SaveMode.Bound)]
    public async Task TimeoutRemainsUnavailableWithoutReplayingSave(SaveMode mode)
    {
        using var handler = new SaveHandler(() => throw new TaskCanceledException("server timed out"));
        using var client = Client(handler);

        var result = await Save(client, mode, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("Unavailable", result.State);
        Assert.Equal(1, handler.Calls);
    }

    private static Task<HonuaOgcStyleSaveResult> Save(HonuaOgcStylesHttpClient client, SaveMode mode, CancellationToken cancellationToken) => mode switch
    {
        SaveMode.Ordinary => client.UpdateStylesheetAsync("shared", HonuaOgcStyleEncoding.MapLibre, "{}", true, cancellationToken),
        SaveMode.Conditional => client.UpdateStylesheetConditionallyAsync("shared", HonuaOgcStyleEncoding.MapLibre, "{}", true,
            "\"style-v7\"", new string('a', 64), cancellationToken),
        SaveMode.Bound => client.UpdateStylesheetBoundAsync("shared", HonuaOgcStyleEncoding.MapLibre, "{}", true,
            "\"style-v7\"", new string('a', 64), "operator-binding", cancellationToken),
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };

    private static HonuaOgcStylesHttpClient Client(HttpMessageHandler handler) => new(new HttpClient(handler),
        new HonuaOgcStylesClientOptions(new Uri("https://honua.test"), AuthorityBindingSupported: true));

    private sealed class SaveHandler(Action send) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Put, request.Method);
            Calls++;
            send();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        }
    }
}
