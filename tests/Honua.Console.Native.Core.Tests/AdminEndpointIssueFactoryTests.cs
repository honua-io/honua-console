using System.Net;
using System.Net.Http.Json;
using Honua.Console.Contracts;

namespace Honua.Console.Native.Core.Tests;

/// <summary>
/// Regression coverage for the admin status→state mapping drift (honua-console#279 PA-239). The mapper was
/// copy-pasted across the admin contract shims and OperateAdminShims drifted: it lacked the Conflict and
/// BadRequest arms, so a 409 or 400 admin response surfaced to operators as "Unavailable" (server down)
/// instead of the correct "Conflict"/"Rejected". These assert the canonical shared mapper — now the single
/// source of truth every shim delegates to.
/// </summary>
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
    [InlineData(HttpStatusCode.ServiceUnavailable, "Unavailable")]
    [InlineData(HttpStatusCode.InternalServerError, "Unavailable")]
    public void MapState_MapsEachStatusToItsCanonicalState(HttpStatusCode statusCode, string expectedState) =>
        Assert.Equal(expectedState, AdminEndpointIssueFactory.MapState(statusCode));

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
