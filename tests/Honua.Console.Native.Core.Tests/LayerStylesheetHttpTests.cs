using System.Net;
using Honua.Console.Contracts;

namespace Honua.Console.Native.Core.Tests;

public sealed class LayerStylesheetHttpTests
{
    [Theory]
    [InlineData(HttpStatusCode.OK, true)]
    [InlineData(HttpStatusCode.Forbidden, false)]
    [InlineData(HttpStatusCode.NotFound, false)]
    public async Task LayerRead_PreservesConfiguredIdentityAndHttpFailure(HttpStatusCode status, bool success)
    {
        using var handler = new LayerHandler(status);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://server.example/") };
        using var client = new HonuaOgcStylesHttpClient(http, new HonuaOgcStylesClientOptions(http.BaseAddress, "layer-reader"));
        var result = await client.GetLayerStylesheetAsync(12);
        Assert.Equal("/api/styles/12.json", handler.Path);
        Assert.Equal("layer-reader", handler.ApiKey);
        Assert.Equal("application/vnd.mapbox.style+json", handler.Accept);
        Assert.Equal(success, result.Issue is null);
        if (success) Assert.Equal(TestMapStyles.Style, result.Data!.Content);
        else Assert.Equal((int)status, result.Issue!.StatusCode);
    }

    [Fact]
    public async Task LayerRead_CallerCancellationRemainsCancellation()
    {
        using var handler = new LayerHandler(HttpStatusCode.OK);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://server.example/") };
        using var client = new HonuaOgcStylesHttpClient(http, new HonuaOgcStylesClientOptions(http.BaseAddress));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetLayerStylesheetAsync(12, new CancellationToken(true)));
    }

    private sealed class LayerHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public string? Path { get; private set; }
        public string? ApiKey { get; private set; }
        public string? Accept { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Path = request.RequestUri!.AbsolutePath;
            ApiKey = request.Headers.TryGetValues("X-API-Key", out var values) ? values.Single() : null;
            Accept = request.Headers.Accept.Single().MediaType;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(TestMapStyles.Style) });
        }
    }
}
