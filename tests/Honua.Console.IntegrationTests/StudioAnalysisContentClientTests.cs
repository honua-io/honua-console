using System.Net;
using Honua.Console.Contracts;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free unit coverage for the analysis-content HTTP shim: transport faults map to an Unavailable
/// issue (never a thrown exception that the builder cannot render), caller cancellation propagates, and the
/// admin-protected status codes map to the builder's missing-permission / not-found / unsupported surfaces.
/// </summary>
public sealed class StudioAnalysisContentClientTests
{
    [Fact]
    public async Task CallerCancellationPropagatesInsteadOfEndpointIssue()
    {
        using var cancellation = new CancellationTokenSource();
        using var client = CreateClient(new ThrowingHandler(_ => new TaskCanceledException("caller canceled")));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.GetItemAsync("analysis-content-1", cancellation.Token));
    }

    [Fact]
    public async Task TransportTimeoutMapsToUnavailableIssue()
    {
        using var client = CreateClient(new ThrowingHandler(_ => new TaskCanceledException("timeout")));

        var result = await client.GetItemAsync("analysis-content-1");

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Issue);
        Assert.Equal("Unavailable", result.Issue.State);
        Assert.Contains("could not be reached", result.Issue.Detail, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "Missing permission")]
    [InlineData(HttpStatusCode.Forbidden, "Missing permission")]
    [InlineData(HttpStatusCode.NotFound, "Not found")]
    [InlineData(HttpStatusCode.BadRequest, "Invalid request")]
    [InlineData(HttpStatusCode.Conflict, "Conflict")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "Unavailable")]
    public async Task ErrorStatusMapsToNamedIssue(HttpStatusCode statusCode, string expectedState)
    {
        using var client = CreateClient(new StatusHandler(statusCode));

        var result = await client.GetItemAsync("analysis-content-1");

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Issue);
        Assert.Equal(expectedState, result.Issue.State);
        Assert.Equal((int)statusCode, result.Issue.StatusCode);
    }

    [Fact]
    public async Task SuccessfulItemResponseDeserializesSnapshotWithoutEnvelope()
    {
        const string json = """
        {
          "item": {
            "itemId": "analysis-content-abc",
            "kind": "analysisPackage",
            "name": "buffer-package",
            "currentVersion": 1,
            "currentVersionId": "analysis-content-abc:v1",
            "lifecycle": "active",
            "createdAt": "2026-05-24T10:00:00Z",
            "updatedAt": "2026-05-24T10:00:00Z"
          },
          "version": {
            "versionId": "analysis-content-abc:v1",
            "itemId": "analysis-content-abc",
            "version": 1,
            "kind": "analysisPackage",
            "analysisPackage": {
              "plan": {
                "planId": "plan-1",
                "intentId": "intent-1",
                "steps": [
                  { "stepId": "step-1", "kind": "Geoprocess", "processId": "geometry.buffer", "inputs": { "distance": "10" }, "dependsOn": [] }
                ],
                "outputs": ["FeatureLayer"]
              },
              "requestedArtifacts": ["FeatureLayer"]
            },
            "contentHash": "abc123",
            "createdAt": "2026-05-24T10:00:00Z"
          }
        }
        """;
        using var client = CreateClient(new JsonHandler(HttpStatusCode.OK, json));

        var result = await client.GetItemAsync("analysis-content-abc");

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        Assert.Equal(AnalysisContentKind.AnalysisPackage, result.Data.Item.Kind);
        Assert.Equal("analysis-content-abc", result.Data.Item.ItemId);
        var step = Assert.Single(result.Data.Version.AnalysisPackage!.Plan.Steps);
        Assert.Equal(AnalysisPlanStepKind.Geoprocess, step.Kind);
        Assert.Equal("geometry.buffer", step.ProcessId);
        Assert.Equal(ArtifactKind.FeatureLayer, Assert.Single(result.Data.Version.AnalysisPackage.RequestedArtifacts));
    }

    private static HttpStudioAnalysisContentClient CreateClient(HttpMessageHandler handler) =>
        new(
            new HttpClient(handler),
            new StudioAnalysisContentClientOptions(new Uri("https://honua.test"), "test-admin-key"));

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

    private sealed class StatusHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;

        public StatusHandler(HttpStatusCode statusCode)
        {
            _statusCode = statusCode;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(_statusCode));
    }

    private sealed class JsonHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _json;

        public JsonHandler(HttpStatusCode statusCode, string json)
        {
            _statusCode = statusCode;
            _json = json;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_json, System.Text.Encoding.UTF8, "application/json")
            });
    }
}
