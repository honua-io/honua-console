using System.Net;
using System.Net.Http.Json;
using Honua.Console.Contracts;

namespace Honua.Console.Shell.Tests;

// A 409 or 400 stays Conflict or Rejected; mapping it to Unavailable would claim the server is down.
public sealed class AdminEndpointIssueFactoryTests
{
    [Theory]
    [InlineData(HttpStatusCode.Conflict, "Conflict")]
    [InlineData(HttpStatusCode.PreconditionFailed, "Conflict")]
    [InlineData(HttpStatusCode.PreconditionRequired, "Conflict")]
    [InlineData(HttpStatusCode.BadRequest, "Rejected")]
    [InlineData(HttpStatusCode.Unauthorized, "Missing permission")]
    [InlineData(HttpStatusCode.Forbidden, "Missing permission")]
    [InlineData(HttpStatusCode.NotFound, "Unsupported")]
    [InlineData(HttpStatusCode.MethodNotAllowed, "Unsupported")]
    [InlineData(HttpStatusCode.NotImplemented, "Unsupported")]
    [InlineData(HttpStatusCode.TooManyRequests, "Throttled")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "Unavailable")]
    [InlineData(HttpStatusCode.InternalServerError, "Unavailable")]
    public void MapState_MapsEachStatusToItsCanonicalState(HttpStatusCode statusCode, string expectedState) =>
        Assert.Equal(expectedState, AdminEndpointIssueFactory.MapState(statusCode));

    [Fact]
    public async Task CreateIssueAsync_429WithRetryAfter_MapsToThrottledWithWaitHint()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.TryAddWithoutValidation("Retry-After", "30");
        response.Headers.TryAddWithoutValidation("Honua-Retryable", "true");

        var issue = await AdminEndpointIssueFactory.CreateIssueAsync("test/contract", response);

        Assert.Equal("Throttled", issue.State);
        Assert.True(issue.Receipt?.Retryable);
        Assert.Equal(30, issue.Receipt?.RetryAfterSeconds);
        Assert.Contains("Wait 30 seconds", issue.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateIssueAsync_503WithHonuaRetryable_ProducesTemporaryUnavailableCopy()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        response.Headers.TryAddWithoutValidation("Honua-Retryable", "true");

        var issue = await AdminEndpointIssueFactory.CreateIssueAsync("test/contract", response);

        Assert.Equal("Unavailable", issue.State);
        Assert.True(issue.Receipt?.Retryable);
        Assert.Contains("temporarily unavailable", issue.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateIssueAsync_503WithHonuaRetryableFalse_ProducesNonRetryableCopy()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        response.Headers.TryAddWithoutValidation("Honua-Retryable", "false");

        var issue = await AdminEndpointIssueFactory.CreateIssueAsync("test/contract", response);

        Assert.Equal("Unavailable", issue.State);
        Assert.False(issue.Receipt?.Retryable);
        Assert.Contains("not retryable", issue.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("temporarily unavailable", issue.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateIssue_409_IsConflict_NotUnavailable()
    {
        var issue = AdminEndpointIssueFactory.CreateIssue("test/contract", HttpStatusCode.Conflict);

        Assert.Equal("Conflict", issue.State);
        Assert.NotEqual("Unavailable", issue.State);
        Assert.Equal(409, issue.StatusCode);
        Assert.Equal("test/contract", issue.Contract);
    }

    [Fact]
    public void CreateIssue_400_IsRejected_NotUnavailable()
    {
        var issue = AdminEndpointIssueFactory.CreateIssue("test/contract", HttpStatusCode.BadRequest);

        Assert.Equal("Rejected", issue.State);
        Assert.NotEqual("Unavailable", issue.State);
        Assert.Equal(400, issue.StatusCode);
    }

    [Fact]
    public async Task CreateIssueAsync_RetainsBodyHeadersAndStructuredFailures()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
        {
            Content = JsonContent.Create(new
            {
                kind = "validation",
                code = "invalid-layer",
                retryable = false,
                errors = new[]
                {
                    new { code = "required", path = "$.layerId", fieldId = "layerId", message = "Required" }
                }
            })
        };
        response.Headers.TryAddWithoutValidation("X-Correlation-ID", "console-validation");

        var issue = await AdminEndpointIssueFactory.CreateIssueAsync("test/contract", response);

        Assert.Equal("Rejected", issue.State);
        Assert.Equal("invalid-layer", issue.Receipt?.Code);
        Assert.Equal("console-validation", issue.Receipt?.CorrelationId);
        var field = Assert.Single(issue.FieldErrors);
        Assert.Equal("layerId", field.FieldId);
    }

    [Fact]
    public async Task OperateAdminGetResponse_RetainsFailureReceipt()
    {
        using var http = new HttpClient(new FailureResponseHandler())
        {
            BaseAddress = new Uri("https://server.example")
        };
        using var client = new HonuaAdminOperateHttpClient(
            http,
            new HonuaAdminOperateClientOptions(http.BaseAddress!));

        var result = await client.ListConnectionsAsync();

        Assert.NotNull(result.Issue);
        Assert.Equal("invalid-admin-request", result.Issue?.Receipt?.Code);
        Assert.Equal("operate-correlation", result.Issue?.Receipt?.CorrelationId);
        Assert.Single(result.Issue?.FieldErrors ?? []);
    }

    private sealed class FailureResponseHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("""
                    {"kind":"validation","code":"invalid-admin-request","errors":[{"fieldId":"serviceName","message":"Required"}]}
                    """)
            };
            response.Headers.TryAddWithoutValidation("X-Correlation-ID", "operate-correlation");
            return Task.FromResult(response);
        }
    }
}
