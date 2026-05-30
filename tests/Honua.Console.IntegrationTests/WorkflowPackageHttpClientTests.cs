using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Honua.Console.Contracts;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free wire-contract coverage for <see cref="HttpWorkflowPackageApiClient"/>, the thin typed HTTP
/// boundary the Studio GP/ETL workflow editor binds to (honua-server #1185, <c>/api/v1/console/workflow-*</c>).
/// The Testcontainers integration test proves end-to-end binding against a live server but skips without
/// Docker, so this suite pins the transport semantics every environment must keep green: exact route/verb,
/// the admin <c>X-API-Key</c> header, request-body serialization, the shared <c>ApiResponse&lt;T&gt;</c>
/// envelope unwrap, and the status-code -> <see cref="WorkflowEndpointIssue"/> mapping (including surfacing the
/// server's own graph-validation failure messages from <c>data.failures</c>). These are real failure/edge
/// assertions, not a smoke test - a regression in any of them ships a broken or dishonest editor binding.
/// </summary>
public sealed class WorkflowPackageHttpClientTests
{
    private static readonly Uri BaseAddress = new("https://honua.test");

    [Fact]
    public async Task GetNodeRegistry_TargetsConsoleRouteWithApiKey_AndUnwrapsEnvelope()
    {
        var handler = new RecordingHandler(_ => Envelope(new WorkflowNodeRegistrySnapshot
        {
            RegistryVersion = "reg-7",
            Nodes =
            [
                new WorkflowNodeDefinition { NodeTypeId = "process:geometry.area", Title = "Area", Category = "Source" }
            ]
        }));
        using var client = CreateClient(handler, apiKey: "admin-secret");

        var result = await client.GetNodeRegistryAsync();

        // Wire contract: exact admin console route + GET, and the admin API key travels as X-API-Key.
        Assert.Equal(HttpMethod.Get, handler.LastRequest!.Method);
        Assert.Equal("/api/v1/console/workflow-node-registry", handler.LastRequest.RequestUri!.AbsolutePath);
        Assert.Equal("admin-secret", Assert.Single(handler.LastRequest.Headers.GetValues("X-API-Key")));
        // The ApiResponse<T> envelope is unwrapped to the bare data the editor consumes.
        Assert.True(result.IsSuccess);
        Assert.Equal("reg-7", result.Data!.RegistryVersion);
        Assert.Equal("process:geometry.area", Assert.Single(result.Data.Nodes).NodeTypeId);
    }

    [Fact]
    public async Task GetNodeRegistry_WithoutApiKey_OmitsHeader()
    {
        var handler = new RecordingHandler(_ => Envelope(new WorkflowNodeRegistrySnapshot()));
        using var client = CreateClient(handler, apiKey: null);

        await client.GetNodeRegistryAsync();

        // An unauthenticated console build must not invent an empty/garbage API key header.
        Assert.False(handler.LastRequest!.Headers.Contains("X-API-Key"));
    }

    [Fact]
    public async Task CreatePackage_PutsRequestBodyOnPostRoute_AndReturnsServerPackage()
    {
        var handler = new RecordingHandler(_ => Envelope(
            new WorkflowPackage { PackageId = "pkg-42", Name = "Parcel normalizer", LatestVersion = null },
            HttpStatusCode.Created));
        using var client = CreateClient(handler);

        var request = new SaveWorkflowPackageRequest
        {
            Name = "Parcel normalizer",
            Namespace = "studio.workflow",
            Graph = new WorkflowGraph
            {
                Nodes = [new WorkflowNode { NodeId = "n1", NodeTypeId = "process:geometry.area" }]
            }
        };

        var result = await client.CreatePackageAsync(request);

        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal("/api/v1/console/workflow-packages", handler.LastRequest.RequestUri!.AbsolutePath);
        // The save request is serialized with camelCase property names the server contract expects.
        using var body = JsonDocument.Parse(handler.LastBody!);
        Assert.Equal("Parcel normalizer", body.RootElement.GetProperty("name").GetString());
        Assert.Equal("process:geometry.area", body.RootElement
            .GetProperty("graph").GetProperty("nodes")[0].GetProperty("nodeTypeId").GetString());
        Assert.True(result.IsSuccess);
        Assert.Equal("pkg-42", result.Data!.PackageId);
    }

    [Fact]
    public async Task GetPackage_EscapesPackageIdInRoute()
    {
        var handler = new RecordingHandler(_ => Envelope(new WorkflowPackage { PackageId = "weird/id" }));
        using var client = CreateClient(handler);

        await client.GetPackageAsync("weird/id");

        // A package id with a slash must not break out of the route segment.
        Assert.Equal("/api/v1/console/workflow-packages/weird%2Fid", handler.LastRequest!.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task CreateVersion_OnGraphValidation400_SurfacesServerFailureMessages_AsValidationFailedIssue()
    {
        // honua-server returns ApiResponse<WorkflowPackageValidationResult>.Failure on a 400, carrying the
        // failing graph rules under data.failures. The client must surface those rule messages (not just the
        // HTTP code) so the editor's blocked surface explains *why* the version was rejected.
        var failurePayload = """
        {
          "success": false,
          "message": "Workflow package validation failed.",
          "data": {
            "isValid": false,
            "failures": [
              { "code": "graph.sink.missing", "message": "At least one sink node is required." },
              { "code": "graph.edge.dangling", "message": "Edge references unknown node 'n9'." }
            ],
            "warnings": []
          }
        }
        """;
        var handler = new RecordingHandler(_ => Raw(HttpStatusCode.BadRequest, failurePayload));
        using var client = CreateClient(handler);

        var result = await client.CreateVersionAsync("pkg-1");

        Assert.EndsWith("/workflow-packages/pkg-1/versions", handler.LastRequest!.RequestUri!.AbsolutePath, StringComparison.Ordinal);
        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Issue!.StatusCode);
        Assert.Equal("Validation failed", result.Issue.State);
        // Both failing rule messages are surfaced so the editor can render them, not just the HTTP status.
        Assert.Contains("At least one sink node is required.", result.Issue.Detail, StringComparison.Ordinal);
        Assert.Contains("Edge references unknown node 'n9'.", result.Issue.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetPackage_On404_MapsToUnsupportedWithStatusCode()
    {
        var handler = new RecordingHandler(_ => Raw(HttpStatusCode.NotFound, "{\"message\":\"not found\"}"));
        using var client = CreateClient(handler);

        var result = await client.GetPackageAsync("missing");

        // A 404 is a missing draft / absent contract: the editor relies on State + StatusCode to tell a
        // not-found draft (renderable empty state) from a true binding failure.
        Assert.False(result.IsSuccess);
        Assert.Equal("Unsupported", result.Issue!.State);
        Assert.Equal(404, result.Issue.StatusCode);
    }

    [Fact]
    public async Task Publish_On409Conflict_MapsToConflictIssue()
    {
        var handler = new RecordingHandler(_ => Raw(HttpStatusCode.Conflict, "{\"message\":\"already published\"}"));
        using var client = CreateClient(handler);

        var result = await client.PublishVersionAsync(
            "pkg-1",
            3,
            new PublishWorkflowPackageRequest { Target = WorkflowPublicationTarget.ProcessEndpoint, ProcessId = "p1" });

        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.EndsWith("/workflow-packages/pkg-1/versions/3/publish", handler.LastRequest.RequestUri!.AbsolutePath, StringComparison.Ordinal);
        Assert.False(result.IsSuccess);
        Assert.Equal("Conflict", result.Issue!.State);
        Assert.Equal(409, result.Issue.StatusCode);
        Assert.True(result.Issue.IsConflict);
    }

    [Fact]
    public async Task DryRun_OnUnauthorized_MapsToMissingPermission()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        using var client = CreateClient(handler);

        var result = await client.DryRunVersionAsync("pkg-1", 2);

        Assert.False(result.IsSuccess);
        Assert.Equal("Missing permission", result.Issue!.State);
        Assert.Equal(401, result.Issue.StatusCode);
    }

    [Fact]
    public async Task RunPublication_OnSuccessFalseEnvelope_ReportsUnavailable_NotFabricatedSuccess()
    {
        // A 200 with success:false (no data) must never be read as a started run; the editor would otherwise
        // show fake Operate evidence. The client maps it to an Unavailable issue carrying the server message.
        var handler = new RecordingHandler(_ => Raw(
            HttpStatusCode.OK,
            "{\"success\":false,\"message\":\"runner is draining\",\"data\":null}"));
        using var client = CreateClient(handler);

        var result = await client.RunPublicationAsync("pub-1", new RunWorkflowPublicationRequest());

        Assert.False(result.IsSuccess);
        Assert.Equal("Unavailable", result.Issue!.State);
        Assert.Contains("runner is draining", result.Issue.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListPackages_OnUnreachableServer_MapsTransportFailureToUnavailable()
    {
        var handler = new ThrowingHandler(new HttpRequestException("connection refused"));
        using var client = CreateClient(handler);

        var result = await client.ListPackagesAsync();

        // A transport failure (server down / DNS / TLS) is Unavailable, never an exception bubbling into the UI.
        Assert.False(result.IsSuccess);
        Assert.Equal("Unavailable", result.Issue!.State);
        Assert.Contains("connection refused", result.Issue.Detail, StringComparison.Ordinal);
    }

    private static HttpWorkflowPackageApiClient CreateClient(HttpMessageHandler handler, string? apiKey = null)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = BaseAddress };
        return new HttpWorkflowPackageApiClient(httpClient, new WorkflowPackageClientOptions(BaseAddress, apiKey));
    }

    private static HttpResponseMessage Envelope<T>(T data, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status)
        {
            Content = JsonContent.Create(new WorkflowApiResponse<T>(true, data, null, DateTimeOffset.UtcNow))
        };

    private static HttpResponseMessage Raw(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        public HttpRequestMessage? LastRequest { get; private set; }

        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            if (request.Content is not null)
            {
                LastBody = await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            }

            return _responder(request);
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        private readonly Exception _exception;

        public ThrowingHandler(Exception exception)
        {
            _exception = exception;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw _exception;
    }
}
