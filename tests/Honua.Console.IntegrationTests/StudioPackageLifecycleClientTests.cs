using Honua.Console.Contracts;

namespace Honua.Console.IntegrationTests;

public sealed class StudioPackageLifecycleClientTests
{
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
            new HttpClient(handler),
            new StudioPackageLifecycleClientOptions(new Uri("https://honua.test"), "test-admin-key"));

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
