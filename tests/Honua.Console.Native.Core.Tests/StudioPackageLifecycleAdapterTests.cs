using System.Net;
using System.Text;
using Honua.Console.Contracts;
using Honua.Sdk.Studio.Packages;

namespace Honua.Console.Native.Core.Tests;

/// <summary>
/// D1 (#265): <see cref="HttpStudioPackageLifecycleClient"/> is a thin adapter over the SDK's
/// <c>HonuaStudioPackageClient</c>, which throws on non-2xx / transport faults. These tests drive the
/// adapter through a stub transport and assert it translates the SDK's throwing contract back into the
/// console's non-throwing <see cref="StudioEndpointResult{T}"/> envelope with the state vocabulary,
/// contract string, status code, conflict flag, and validation diagnostics preserved.
/// </summary>
public sealed class StudioPackageLifecycleAdapterTests
{
    private static readonly Uri BaseUri = new("https://honua.test");
    private const string ValidateContract = "POST /api/v1/studio/package-drafts/{draftId}/validate";

    [Fact]
    public async Task ValidateAsync_TranslatesSdkErrorIntoIssue_PreservingStateContractStatusAndDiagnostics()
    {
        const string body =
            """
            {"status":"invalid","diagnostics":[{"code":"studio.map.layer.missing","severity":"error","path":"/body/layers/0","message":"Layer binding is required."}]}
            """;
        using var client = CreateClient(new StubHandler(HttpStatusCode.Conflict, body));

        var result = await client.ValidatePackageDraftAsync(Guid.NewGuid());

        Assert.False(result.IsSuccess);
        var issue = result.Issue!;
        Assert.Equal("Conflict", issue.State);
        Assert.True(issue.IsConflict);
        Assert.Equal((int)HttpStatusCode.Conflict, issue.StatusCode);
        Assert.Equal(ValidateContract, issue.Contract);

        var diagnostic = Assert.Single(issue.Diagnostics);
        Assert.Equal("studio.map.layer.missing", diagnostic.Code);
        Assert.Equal(StudioPackageDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("/body/layers/0", diagnostic.Path);
    }

    [Fact]
    public async Task ValidateAsync_MapsForbiddenToMissingPermission()
    {
        using var client = CreateClient(new StubHandler(HttpStatusCode.Forbidden, "{}"));

        var result = await client.ValidatePackageDraftAsync(Guid.NewGuid());

        Assert.False(result.IsSuccess);
        Assert.Equal("Missing permission", result.Issue!.State);
        Assert.Equal((int)HttpStatusCode.Forbidden, result.Issue.StatusCode);
        Assert.Empty(result.Issue.Diagnostics);
    }

    [Fact]
    public async Task ValidateAsync_ReturnsDataOnSuccessEnvelope()
    {
        const string body =
            """
            {"success":true,"data":{"status":"valid","diagnostics":[],"unsupportedCapabilities":[],"generatedAt":null},"message":null,"timestamp":"2024-01-01T00:00:00+00:00"}
            """;
        using var client = CreateClient(new StubHandler(HttpStatusCode.OK, body));

        var result = await client.ValidatePackageDraftAsync(Guid.NewGuid());

        Assert.True(result.IsSuccess);
        Assert.Null(result.Issue);
        Assert.Equal(StudioPackageValidationStatus.Valid, result.Data!.Status);
    }

    [Fact]
    public async Task ValidateAsync_MapsTransportFailureToUnavailable()
    {
        using var client = CreateClient(new ThrowingHandler(new HttpRequestException("connection refused")));

        var result = await client.ValidatePackageDraftAsync(Guid.NewGuid());

        Assert.False(result.IsSuccess);
        Assert.Equal("Unavailable", result.Issue!.State);
        Assert.Null(result.Issue.StatusCode);
        Assert.Equal(ValidateContract, result.Issue.Contract);
    }

    private static HttpStudioPackageLifecycleClient CreateClient(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = BaseUri };
        return new HttpStudioPackageLifecycleClient(
            httpClient,
            new StudioPackageLifecycleClientOptions(BaseUri, "admin-key"));
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;

        public StubHandler(HttpStatusCode status, string body)
        {
            _status = status;
            _body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json")
            });
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        private readonly Exception _exception;

        public ThrowingHandler(Exception exception) => _exception = exception;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw _exception;
    }
}
