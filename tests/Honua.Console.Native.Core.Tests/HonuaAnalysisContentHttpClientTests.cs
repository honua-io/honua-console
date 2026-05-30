using System.Net;
using System.Text;
using System.Text.Json;
using Honua.Console.Contracts;

namespace Honua.Console.Native.Core.Tests;

/// <summary>
/// Transport-level coverage for <see cref="HonuaAnalysisContentHttpClient"/>, the thin HttpClient behind the
/// single Honua.Console.Contracts boundary that binds the Studio analysis builder (honua-console#53) to the
/// shipped honua-server analysis content/artifacts contract (honua-server#1182) and the closed execution
/// engine (honua-server#681). These tests drive the real client over a recording <see cref="HttpMessageHandler"/>
/// and feed it byte-for-byte the JSON shapes the live server emits (PascalCase string enums from
/// <c>AnalysisContentApiJsonContext</c>: <c>"kind":"FeatureLayer"</c>, <c>"status":"Failed"</c>; camelCase
/// content kind <c>"analysisPackage"</c>; admin ProblemDetails error bodies), so a drift between the Console
/// wire records and the server document graph fails here rather than only in the opt-in Docker integration
/// test. They assert the exact route + verb + admin API-key header for every bound endpoint, the request body
/// the authoring path lowers, and the HTTP-status -> capability-state mapping the data source surfaces.
/// </summary>
public sealed class HonuaAnalysisContentHttpClientTests
{
    private static readonly Uri BaseUri = new("https://server.example");

    [Fact]
    public async Task CreateItem_PostsToItemsRoute_WithAdminKeyAndAnalysisPackageBody()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.Created, ServerVersionJson("analysis-1", 1)));
        var client = CreateClient(handler, apiKey: "admin-secret");

        var request = new HonuaCreateAnalysisContentItemRequest
        {
            Kind = HonuaAnalysisContentKinds.AnalysisPackage,
            Name = "hydrant-buffer",
            Title = "Hydrant buffer",
            AnalysisPackage = new HonuaAnalysisPackageContent
            {
                Plan = new HonuaAnalysisPlan
                {
                    PlanId = "plan-1",
                    IntentId = "intent-1",
                    Steps =
                    [
                        new HonuaAnalysisPlanStep
                        {
                            StepId = "method",
                            Kind = HonuaAnalysisPlanStepKinds.Geoprocess,
                            ProcessId = "buffer"
                        }
                    ]
                },
                RequestedArtifacts = [HonuaArtifactKinds.FeatureLayer]
            }
        };

        var result = await client.CreateItemAsync(request);

        Assert.Null(result.Issue);
        Assert.NotNull(result.Data);
        Assert.Equal("analysis-1", result.Data!.Item.ItemId);

        var recorded = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, recorded.Method);
        Assert.Equal("/api/v1/analysis/content/items", recorded.Uri!.AbsolutePath);
        Assert.Equal("admin-secret", Assert.Single(recorded.ApiKeyValues));

        // The lowered body must carry the camelCase content kind the server enum expects and the method step,
        // proving the authoring path serializes into the real server document (not a Console-only shape).
        using var body = JsonDocument.Parse(recorded.Body!);
        Assert.Equal("analysisPackage", body.RootElement.GetProperty("kind").GetString());
        Assert.Equal(
            "buffer",
            body.RootElement
                .GetProperty("analysisPackage")
                .GetProperty("plan")
                .GetProperty("steps")[0]
                .GetProperty("processId")
                .GetString());
    }

    [Fact]
    public async Task GetVersion_Latest_UsesVersionsLatestRoute()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK, ServerVersionJson("analysis-1", 5)));
        var client = CreateClient(handler);

        var result = await client.GetVersionAsync("analysis-1", version: null);

        Assert.Null(result.Issue);
        Assert.Equal(5, result.Data!.Version.Version);
        var recorded = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, recorded.Method);
        Assert.Equal("/api/v1/analysis/content/items/analysis-1/versions/latest", recorded.Uri!.AbsolutePath);
    }

    [Fact]
    public async Task GetVersion_Explicit_UsesNumberedVersionRoute()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK, ServerVersionJson("analysis-1", 3)));
        var client = CreateClient(handler);

        var result = await client.GetVersionAsync("analysis-1", version: 3);

        Assert.Null(result.Issue);
        var recorded = Assert.Single(handler.Requests);
        Assert.Equal("/api/v1/analysis/content/items/analysis-1/versions/3", recorded.Uri!.AbsolutePath);
    }

    [Fact]
    public async Task GetVersion_DeserializesServerStringEnums_ToContractStrings()
    {
        // The live server serializes AnalysisContentKind via [JsonStringEnumMemberName] -> "analysisPackage",
        // and the geoprocessing step/artifact enums via UseStringEnumConverter -> PascalCase. The Console wire
        // records read those enum strings as plain strings; this pins both casings so a server enum-policy
        // change is caught at the boundary.
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK, ServerVersionJson("analysis-1", 2)));
        var client = CreateClient(handler);

        var result = await client.GetVersionAsync("analysis-1", version: 2);

        var version = result.Data!.Version;
        Assert.Equal("analysisPackage", version.Kind);
        var package = Assert.IsType<HonuaAnalysisPackageContent>(version.AnalysisPackage);
        Assert.Contains(package.Plan.Steps, s => s.Kind == HonuaAnalysisPlanStepKinds.QueryFeatures);
        Assert.Contains(package.Plan.Steps, s => s.Kind == HonuaAnalysisPlanStepKinds.Geoprocess);
        Assert.Contains(HonuaArtifactKinds.FeatureLayer, package.RequestedArtifacts);
    }

    [Fact]
    public async Task Run_PostsToRunsRoute_WithIdempotencyKeyAndParameters()
    {
        var responseJson = $$"""
            {"jobId":"job-77","status":"Queued","version":{{ServerVersionInner("analysis-1", 3)}}}
            """;
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK, responseJson));
        var client = CreateClient(handler);

        var result = await client.RunAsync(
            "analysis-1",
            3,
            new HonuaRunAnalysisContentVersionRequest
            {
                IdempotencyKey = "console-analysis-1-v3",
                Parameters = new Dictionary<string, string> { ["distanceMeters"] = "250" }
            });

        Assert.Null(result.Issue);
        Assert.Equal("job-77", result.Data!.JobId);
        Assert.Equal("Queued", result.Data.Status);

        var recorded = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, recorded.Method);
        Assert.Equal("/api/v1/analysis/content/items/analysis-1/versions/3/runs", recorded.Uri!.AbsolutePath);
        using var body = JsonDocument.Parse(recorded.Body!);
        Assert.Equal("console-analysis-1-v3", body.RootElement.GetProperty("idempotencyKey").GetString());
        Assert.Equal("250", body.RootElement.GetProperty("parameters").GetProperty("distanceMeters").GetString());
    }

    [Fact]
    public async Task GetArtifact_ParsesServerArtifactAndBinding_WithPascalCaseKind()
    {
        var artifactJson = """
            {
              "artifact": {
                "artifactId":"artifact-9","resultPackageId":"rp-1","jobId":"job-77",
                "sourceItemId":"analysis-1","sourceVersion":3,"sourceVersionId":"analysis-1-v3",
                "kind":"FeatureLayer","label":"Buffered hydrants","contentType":"application/geo+json",
                "byteSize":2048,"retentionState":"retained","promotionState":"none",
                "createdAt":"2026-05-29T12:00:00Z"
              },
              "binding": { "artifactId":"artifact-9","role":"dataSource","targetKind":"content","targetSlot":"source" }
            }
            """;
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK, artifactJson));
        var client = CreateClient(handler);

        var result = await client.GetArtifactAsync("artifact-9");

        Assert.Null(result.Issue);
        Assert.Equal("artifact-9", result.Data!.Artifact.ArtifactId);
        Assert.Equal(HonuaArtifactKinds.FeatureLayer, result.Data.Artifact.Kind);
        Assert.Equal("retained", result.Data.Artifact.RetentionState);
        Assert.Equal(2048, result.Data.Artifact.ByteSize);
        var recorded = Assert.Single(handler.Requests);
        Assert.Equal("/api/v1/analysis/artifacts/artifact-9", recorded.Uri!.AbsolutePath);
    }

    [Fact]
    public async Task GetJobFailure_ParsesSafeClassification_FromJobsFailureRoute()
    {
        var failureJson = """
            {"jobId":"job-77","classification":"validationFailed","message":"The plan failed validation.","isTerminal":true}
            """;
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK, failureJson));
        var client = CreateClient(handler);

        var result = await client.GetJobFailureAsync("job-77");

        Assert.Null(result.Issue);
        Assert.Equal("validationFailed", result.Data!.Classification);
        Assert.True(result.Data.IsTerminal);
        var recorded = Assert.Single(handler.Requests);
        Assert.Equal("/api/v1/analysis/jobs/job-77/failure", recorded.Uri!.AbsolutePath);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "Rejected")]
    [InlineData(HttpStatusCode.NotFound, "Unsupported")]
    [InlineData(HttpStatusCode.Conflict, "Conflict")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "Unavailable")]
    [InlineData(HttpStatusCode.Unauthorized, "Missing permission")]
    [InlineData(HttpStatusCode.Forbidden, "Missing permission")]
    public async Task Run_ServerError_MapsStatusToCapabilityState(HttpStatusCode status, string expectedState)
    {
        // The server returns admin ProblemDetails error bodies; the client maps the status code to the neutral
        // capability-state vocabulary the analysis builder surfaces, never throwing or fabricating success.
        var handler = new RecordingHandler(_ => ProblemDetails(status));
        var client = CreateClient(handler);

        var result = await client.RunAsync(
            "analysis-1",
            3,
            new HonuaRunAnalysisContentVersionRequest { IdempotencyKey = "k" });

        Assert.Null(result.Data);
        Assert.NotNull(result.Issue);
        Assert.Equal(expectedState, result.Issue!.State);
        Assert.Equal((int)status, result.Issue.StatusCode);
    }

    [Fact]
    public async Task GetVersion_TransportFailure_SurfacesUnavailableWithoutThrowing()
    {
        var handler = new RecordingHandler(_ => throw new HttpRequestException("connection refused"));
        var client = CreateClient(handler);

        var result = await client.GetVersionAsync("analysis-1", version: null);

        Assert.Null(result.Data);
        Assert.Equal("Unavailable", result.Issue!.State);
        Assert.Contains("could not be reached", result.Issue.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetVersion_MalformedBody_SurfacesUnsupportedContractMismatch()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{ this is not valid json", Encoding.UTF8, "application/json")
        });
        var client = CreateClient(handler);

        var result = await client.GetVersionAsync("analysis-1", version: 1);

        Assert.Null(result.Data);
        Assert.Equal("Unsupported", result.Issue!.State);
    }

    [Fact]
    public async Task Client_OmitsApiKeyHeader_WhenNoKeyConfigured()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK, ServerVersionJson("analysis-1", 1)));
        var client = CreateClient(handler, apiKey: null);

        await client.GetVersionAsync("analysis-1", version: null);

        var recorded = Assert.Single(handler.Requests);
        Assert.Empty(recorded.ApiKeyValues);
    }

    private static HonuaAnalysisContentHttpClient CreateClient(HttpMessageHandler handler, string? apiKey = null)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = BaseUri };
        return new HonuaAnalysisContentHttpClient(httpClient, new HonuaAnalysisContentClientOptions(BaseUri, apiKey));
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage ProblemDetails(HttpStatusCode status) =>
        new(status)
        {
            Content = new StringContent(
                $$"""{"type":"about:blank","title":"error","status":{{(int)status}},"detail":"server says no"}""",
                Encoding.UTF8,
                "application/problem+json")
        };

    // A server analysis-package version body, mirroring AnalysisContentVersionResponse with the live enum
    // casings: content kind camelCase ("analysisPackage"), geoprocessing step/artifact kinds PascalCase.
    private static string ServerVersionJson(string itemId, int version) =>
        $$"""
        {
          "item": {
            "itemId":"{{itemId}}","kind":"analysisPackage","name":"hydrant-buffer","title":"Hydrant buffer",
            "visibility":"organization","currentVersion":{{version}},"currentVersionId":"{{itemId}}-v{{version}}",
            "lifecycle":"active","createdAt":"2026-05-29T10:00:00Z","updatedAt":"2026-05-29T10:00:00Z"
          },
          "version": {{ServerVersionInner(itemId, version)}}
        }
        """;

    private static string ServerVersionInner(string itemId, int version) =>
        $$"""
        {
          "versionId":"{{itemId}}-v{{version}}","itemId":"{{itemId}}","version":{{version}},"kind":"analysisPackage",
          "contentHash":"hash-{{version}}","createdAt":"2026-05-29T10:00:00Z",
          "analysisPackage": {
            "intent": { "intentId":"intent-1","goal":"Buffer hydrants","mode":"analysis","requestedOutputs":["FeatureLayer"],"inputs":["hydrants/2"] },
            "plan": {
              "planId":"plan-1","intentId":"intent-1",
              "steps": [
                { "stepId":"load-0","kind":"QueryFeatures","inputs":{"serviceId":"hydrants","layerId":"2","role":"primary"},"dependsOn":[] },
                { "stepId":"method","kind":"Geoprocess","processId":"buffer","inputs":{"method":"buffer"},"dependsOn":["load-0"] }
              ],
              "outputs":["FeatureLayer"],"warnings":[]
            },
            "parameters":{"distanceMeters":"250"},
            "requestedArtifacts":["FeatureLayer"],
            "bindingHints":[{"artifactId":"","role":"output","targetKind":"layer","targetSlot":"source"}],
            "metadata":{"console.method":"buffer","console.outputContentType":"layer","console.outputSchema":"buffer_distance:double"}
          }
        }
        """;

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string? body = null;
            if (request.Content is not null)
            {
                body = await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            }

            var apiKeys = request.Headers.TryGetValues("X-API-Key", out var values)
                ? values.ToArray()
                : [];

            Requests.Add(new RecordedRequest(request.Method, request.RequestUri, body, apiKeys));
            return responder(request);
        }
    }

    private sealed record RecordedRequest(
        HttpMethod Method,
        Uri? Uri,
        string? Body,
        IReadOnlyList<string> ApiKeyValues);
}
