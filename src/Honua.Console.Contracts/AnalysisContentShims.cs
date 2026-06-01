using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Honua.Console.Contracts;

// SHIM(honua-server#1182 / honua-sdk-dotnet): honua-server owns the analysis content/artifacts
// contract (AnalysisContent* records in Honua.Core.Features.AnalysisContent.Domain, admin lifecycle
// under /api/v1/analysis/content and artifact resolution under /api/v1/analysis/artifacts). The
// execution engine that runs a submitted analysis package is the closed honua-server#681/#721/#724
// geoprocessing runtime, consumed through the content "run" route. honua-sdk-dotnet does not yet
// project these as a consumable stable package, and honua-console wires no SDK NuGet feed (see
// SDK_SHIM_POLICY.md). Per the Console Patterns Charter section 11 and SDK_SHIM_POLICY, the analysis
// authoring surface binds through a thin HttpClient behind this single Honua.Console.Contracts
// boundary: the wire records below mirror the server document graph, and the client speaks the real
// admin lifecycle. Do not add a sibling-repo ProjectReference. Swap these for SDK types when
// honua-sdk-dotnet ships a consumable analysis-content projection and honua-console#7 wires the feed.
//
// The analysis content routes are admin routes, so this client reuses the shared
// HonuaAdminEndpointResult<T>/HonuaAdminEndpointIssue envelope (defined in OperateAdminShims.cs).
// Like the forms endpoints they return the DTO directly rather than a {success,data,message} wrapper,
// so this client deserializes the body straight into the contract type and maps analysis content
// status semantics (400 validation, 404 not found, 409 conflict, 503 store unavailable) to issues.
//
// KNOWN SERVER GAP (honua-server#1182): the contract exposes no analysis-package list route
// (GET /api/v1/analysis/content/items has no list verb) and no runtime/cost ESTIMATE route. The
// Console binds every route that exists; the list and estimate capabilities are surfaced as explicit
// "Unsupported" capability states until honua-server adds them, never fabricated.
public sealed record HonuaAnalysisContentClientOptions(Uri BaseUri, string? ApiKey = null);

public interface IHonuaAnalysisContentClient
{
    Uri BaseUri { get; }

    Task<HonuaAdminEndpointResult<HonuaAnalysisContentVersionResponse>> CreateItemAsync(
        HonuaCreateAnalysisContentItemRequest request,
        CancellationToken cancellationToken = default);

    Task<HonuaAdminEndpointResult<HonuaAnalysisContentVersionResponse>> GetItemAsync(
        string itemId,
        CancellationToken cancellationToken = default);

    Task<HonuaAdminEndpointResult<HonuaAnalysisContentVersionResponse>> GetVersionAsync(
        string itemId,
        int? version,
        CancellationToken cancellationToken = default);

    Task<HonuaAdminEndpointResult<HonuaAnalysisContentVersionResponse>> CreateVersionAsync(
        string itemId,
        HonuaCreateAnalysisContentVersionRequest request,
        CancellationToken cancellationToken = default);

    Task<HonuaAdminEndpointResult<HonuaSavedQueryPreviewResult>> PreviewSavedQueryAsync(
        string itemId,
        int version,
        int? limit,
        CancellationToken cancellationToken = default);

    Task<HonuaAdminEndpointResult<HonuaAnalysisContentJobResponse>> RunAsync(
        string itemId,
        int version,
        HonuaRunAnalysisContentVersionRequest request,
        CancellationToken cancellationToken = default);

    Task<HonuaAdminEndpointResult<HonuaAnalysisArtifactResponse>> GetArtifactAsync(
        string artifactId,
        CancellationToken cancellationToken = default);

    Task<HonuaAdminEndpointResult<HonuaAnalysisJobFailure>> GetJobFailureAsync(
        string jobId,
        CancellationToken cancellationToken = default);
}

public sealed class HonuaAnalysisContentHttpClient : IHonuaAnalysisContentClient, IDisposable
{
    private const string ContentBase = "/api/v1/analysis/content";
    private const string ArtifactBase = "/api/v1/analysis/artifacts";
    private const string JobBase = "/api/v1/analysis/jobs";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly string? _apiKey;

    public HonuaAnalysisContentHttpClient(HttpClient httpClient, HonuaAnalysisContentClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);

        BaseUri = options.BaseUri;
        _apiKey = options.ApiKey;
        _httpClient = httpClient;
        _httpClient.BaseAddress ??= BaseUri;
    }

    public Uri BaseUri { get; }

    public Task<HonuaAdminEndpointResult<HonuaAnalysisContentVersionResponse>> CreateItemAsync(
        HonuaCreateAnalysisContentItemRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return SendAsync<HonuaAnalysisContentVersionResponse>(
            HttpMethod.Post,
            $"{ContentBase}/items",
            "POST /api/v1/analysis/content/items",
            body: request,
            cancellationToken: cancellationToken);
    }

    public Task<HonuaAdminEndpointResult<HonuaAnalysisContentVersionResponse>> GetItemAsync(
        string itemId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);

        return SendAsync<HonuaAnalysisContentVersionResponse>(
            HttpMethod.Get,
            $"{ContentBase}/items/{Uri.EscapeDataString(itemId)}",
            "GET /api/v1/analysis/content/items/{itemId}",
            cancellationToken: cancellationToken);
    }

    public Task<HonuaAdminEndpointResult<HonuaAnalysisContentVersionResponse>> GetVersionAsync(
        string itemId,
        int? version,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);

        var path = version is null
            ? $"{ContentBase}/items/{Uri.EscapeDataString(itemId)}/versions/latest"
            : $"{ContentBase}/items/{Uri.EscapeDataString(itemId)}/versions/{Version(version.Value)}";
        var contract = version is null
            ? "GET /api/v1/analysis/content/items/{itemId}/versions/latest"
            : "GET /api/v1/analysis/content/items/{itemId}/versions/{contentVersion}";

        return SendAsync<HonuaAnalysisContentVersionResponse>(
            HttpMethod.Get,
            path,
            contract,
            cancellationToken: cancellationToken);
    }

    public Task<HonuaAdminEndpointResult<HonuaAnalysisContentVersionResponse>> CreateVersionAsync(
        string itemId,
        HonuaCreateAnalysisContentVersionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);
        ArgumentNullException.ThrowIfNull(request);

        return SendAsync<HonuaAnalysisContentVersionResponse>(
            HttpMethod.Post,
            $"{ContentBase}/items/{Uri.EscapeDataString(itemId)}/versions",
            "POST /api/v1/analysis/content/items/{itemId}/versions",
            body: request,
            cancellationToken: cancellationToken);
    }

    public Task<HonuaAdminEndpointResult<HonuaSavedQueryPreviewResult>> PreviewSavedQueryAsync(
        string itemId,
        int version,
        int? limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);

        return SendAsync<HonuaSavedQueryPreviewResult>(
            HttpMethod.Post,
            $"{ContentBase}/items/{Uri.EscapeDataString(itemId)}/versions/{Version(version)}/preview",
            "POST /api/v1/analysis/content/items/{itemId}/versions/{contentVersion}/preview",
            body: new HonuaPreviewSavedQueryRequest { Limit = limit },
            cancellationToken: cancellationToken);
    }

    public Task<HonuaAdminEndpointResult<HonuaAnalysisContentJobResponse>> RunAsync(
        string itemId,
        int version,
        HonuaRunAnalysisContentVersionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);
        ArgumentNullException.ThrowIfNull(request);

        return SendAsync<HonuaAnalysisContentJobResponse>(
            HttpMethod.Post,
            $"{ContentBase}/items/{Uri.EscapeDataString(itemId)}/versions/{Version(version)}/runs",
            "POST /api/v1/analysis/content/items/{itemId}/versions/{contentVersion}/runs",
            body: request,
            cancellationToken: cancellationToken);
    }

    public Task<HonuaAdminEndpointResult<HonuaAnalysisArtifactResponse>> GetArtifactAsync(
        string artifactId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);

        return SendAsync<HonuaAnalysisArtifactResponse>(
            HttpMethod.Get,
            $"{ArtifactBase}/{Uri.EscapeDataString(artifactId)}",
            "GET /api/v1/analysis/artifacts/{artifactId}",
            cancellationToken: cancellationToken);
    }

    public Task<HonuaAdminEndpointResult<HonuaAnalysisJobFailure>> GetJobFailureAsync(
        string jobId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);

        return SendAsync<HonuaAnalysisJobFailure>(
            HttpMethod.Get,
            $"{JobBase}/{Uri.EscapeDataString(jobId)}/failure",
            "GET /api/v1/analysis/jobs/{jobId}/failure",
            cancellationToken: cancellationToken);
    }

    public void Dispose() => _httpClient.Dispose();

    private static string Version(int version) => version.ToString(CultureInfo.InvariantCulture);

    private async Task<HonuaAdminEndpointResult<T>> SendAsync<T>(
        HttpMethod method,
        string path,
        string contract,
        object? body = null,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(method, path);
        if (!string.IsNullOrWhiteSpace(_apiKey))
        {
            request.Headers.TryAddWithoutValidation("X-API-Key", _apiKey);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType(), mediaType: null, JsonOptions);
        }

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            return HonuaAdminEndpointResult<T>.FromIssue(new HonuaAdminEndpointIssue(
                "Unavailable",
                contract,
                $"The Honua server analysis content endpoint could not be reached: {ex.Message}"));
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // A TaskCanceledException whose token is not the caller's is an HttpClient timeout, not caller
            // cancellation, so surface it as Unavailable. Caller-requested cancellation propagates so it
            // cancels the calling operation instead of being masked as a transport failure.
            return HonuaAdminEndpointResult<T>.FromIssue(new HonuaAdminEndpointIssue(
                "Unavailable",
                contract,
                $"The Honua server analysis content endpoint could not be reached: {ex.Message}"));
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var issue = CreateIssue(contract, response.StatusCode);

                // A 400 carries the shared field-addressable validation contract (RFC-7807 ProblemDetails
                // errors[] of FieldValidationError, honua-server Wave 4); parse it so Console can bind each
                // finding onto the offending plan input rather than only surfacing the flat detail.
                if (response.StatusCode == HttpStatusCode.BadRequest)
                {
                    var fieldErrors = await ReadFieldErrorsAsync(response, cancellationToken).ConfigureAwait(false);
                    if (fieldErrors.Count > 0)
                    {
                        issue = issue with { FieldErrors = fieldErrors };
                    }
                }

                return HonuaAdminEndpointResult<T>.FromIssue(issue);
            }

            T? payload;
            try
            {
                payload = await response.Content
                    .ReadFromJsonAsync<T>(JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (JsonException ex)
            {
                return HonuaAdminEndpointResult<T>.FromIssue(new HonuaAdminEndpointIssue(
                    "Unsupported",
                    contract,
                    $"The Honua server analysis content response did not match the expected contract: {ex.Message}",
                    (int)response.StatusCode));
            }

            return payload is null
                ? HonuaAdminEndpointResult<T>.FromIssue(new HonuaAdminEndpointIssue(
                    "Unavailable",
                    contract,
                    "The Honua server analysis content response body was empty.",
                    (int)response.StatusCode))
                : HonuaAdminEndpointResult<T>.FromData(payload);
        }
    }

    // Parses the RFC-7807 ProblemDetails errors[] extension into the shared field-validation shape. Returns
    // an empty list for a body that is not a field-addressable validation rejection (flat ProblemDetails,
    // empty body, malformed JSON). Mirrors the ContentPublication client's parser.
    private static async Task<IReadOnlyList<HonuaFieldValidationError>> ReadFieldErrorsAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        string body;
        try
        {
            body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or IOException)
        {
            return [];
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("errors", out var errors)
                || errors.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var parsed = new List<HonuaFieldValidationError>();
            foreach (var item in errors.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var error = item.Deserialize<HonuaFieldValidationError>(JsonOptions);
                if (error is not null && !string.IsNullOrEmpty(error.Code))
                {
                    parsed.Add(error);
                }
            }

            return parsed;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static HonuaAdminEndpointIssue CreateIssue(string contract, HttpStatusCode statusCode)
    {
        var state = statusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "Missing permission",
            HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotImplemented => "Unsupported",
            HttpStatusCode.Conflict or HttpStatusCode.PreconditionRequired or HttpStatusCode.PreconditionFailed => "Conflict",
            HttpStatusCode.BadRequest => "Rejected",
            HttpStatusCode.ServiceUnavailable => "Unavailable",
            _ => "Unavailable"
        };

        var detail = statusCode switch
        {
            HttpStatusCode.Unauthorized => "The Honua server rejected the request because admin authentication is missing.",
            HttpStatusCode.Forbidden => "The Honua server rejected the request because the current principal lacks analysis content admin permission.",
            HttpStatusCode.NotFound => "The Honua server analysis content item, version, artifact, or job was not found.",
            HttpStatusCode.MethodNotAllowed => "The Honua server exposes the analysis content route but not the required verb.",
            HttpStatusCode.NotImplemented => "The Honua server reports the analysis content capability is not implemented.",
            HttpStatusCode.Conflict => "The Honua server reported a conflict for the analysis content request; reload before retrying.",
            HttpStatusCode.BadRequest => "The Honua server rejected the analysis package; resolve the reported plan/validation issues before submit.",
            HttpStatusCode.ServiceUnavailable => "The Honua server analysis content store or execution engine is currently unavailable.",
            _ => string.Format(
                CultureInfo.InvariantCulture,
                "The Honua server returned HTTP {0} ({1}) for the analysis content request.",
                (int)statusCode,
                statusCode)
        };

        return new HonuaAdminEndpointIssue(state, contract, detail, (int)statusCode);
    }
}

// --- Wire records mirroring honua-server Honua.Core.Features.AnalysisContent.Domain. ---

public static class HonuaAnalysisContentKinds
{
    public const string SavedQuery = "savedQuery";
    public const string AnalysisPackage = "analysisPackage";
}

public static class HonuaAnalysisPlanStepKinds
{
    public const string QueryFeatures = "QueryFeatures";
    public const string Geoprocess = "Geoprocess";
    public const string Aggregate = "Aggregate";
    public const string RenderMap = "RenderMap";
    public const string Export = "Export";
}

public static class HonuaArtifactKinds
{
    public const string Scalar = "Scalar";
    public const string FeatureLayer = "FeatureLayer";
    public const string Table = "Table";
    public const string Raster = "Raster";
    public const string File = "File";
    public const string Report = "Report";
    public const string Map = "Map";
    public const string AppBundle = "AppBundle";
}

public sealed record HonuaCreateAnalysisContentItemRequest
{
    [JsonPropertyName("kind")]
    public string Kind { get; init; } = HonuaAnalysisContentKinds.AnalysisPackage;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("savedQuery")]
    public HonuaSavedQueryContent? SavedQuery { get; init; }

    [JsonPropertyName("analysisPackage")]
    public HonuaAnalysisPackageContent? AnalysisPackage { get; init; }
}

public sealed record HonuaCreateAnalysisContentVersionRequest
{
    [JsonPropertyName("savedQuery")]
    public HonuaSavedQueryContent? SavedQuery { get; init; }

    [JsonPropertyName("analysisPackage")]
    public HonuaAnalysisPackageContent? AnalysisPackage { get; init; }

    [JsonPropertyName("basedOnVersionId")]
    public string? BasedOnVersionId { get; init; }
}

public sealed record HonuaPreviewSavedQueryRequest
{
    [JsonPropertyName("limit")]
    public int? Limit { get; init; }
}

public sealed record HonuaRunAnalysisContentVersionRequest
{
    [JsonPropertyName("idempotencyKey")]
    public string? IdempotencyKey { get; init; }

    [JsonPropertyName("parameters")]
    public Dictionary<string, string>? Parameters { get; init; }
}

public sealed record HonuaAnalysisContentVersionResponse
{
    [JsonPropertyName("item")]
    public HonuaAnalysisContentItem Item { get; init; } = new();

    [JsonPropertyName("version")]
    public HonuaAnalysisContentVersion Version { get; init; } = new();
}

public sealed record HonuaAnalysisContentJobResponse
{
    [JsonPropertyName("jobId")]
    public string JobId { get; init; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    [JsonPropertyName("version")]
    public HonuaAnalysisContentVersion Version { get; init; } = new();
}

public sealed record HonuaAnalysisArtifactResponse
{
    [JsonPropertyName("artifact")]
    public HonuaResultArtifactRecord Artifact { get; init; } = new();

    [JsonPropertyName("binding")]
    public HonuaArtifactBindingRef Binding { get; init; } = new();
}

public sealed record HonuaAnalysisContentItem
{
    [JsonPropertyName("itemId")]
    public string ItemId { get; init; } = string.Empty;

    [JsonPropertyName("kind")]
    public string Kind { get; init; } = HonuaAnalysisContentKinds.AnalysisPackage;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("visibility")]
    public string Visibility { get; init; } = "organization";

    [JsonPropertyName("currentVersion")]
    public int CurrentVersion { get; init; }

    [JsonPropertyName("currentVersionId")]
    public string CurrentVersionId { get; init; } = string.Empty;

    [JsonPropertyName("lifecycle")]
    public string Lifecycle { get; init; } = "active";

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; init; }

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed record HonuaAnalysisContentVersion
{
    [JsonPropertyName("versionId")]
    public string VersionId { get; init; } = string.Empty;

    [JsonPropertyName("itemId")]
    public string ItemId { get; init; } = string.Empty;

    [JsonPropertyName("version")]
    public int Version { get; init; }

    [JsonPropertyName("kind")]
    public string Kind { get; init; } = HonuaAnalysisContentKinds.AnalysisPackage;

    [JsonPropertyName("savedQuery")]
    public HonuaSavedQueryContent? SavedQuery { get; init; }

    [JsonPropertyName("analysisPackage")]
    public HonuaAnalysisPackageContent? AnalysisPackage { get; init; }

    [JsonPropertyName("contentHash")]
    public string ContentHash { get; init; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; init; }
}

// --- Saved-query content graph mirroring honua-server SavedQueryContent / FilterPlan. ---

public static class HonuaFilterPlanCombinators
{
    public const string And = "and";
    public const string Or = "or";
}

public static class HonuaFilterClauseTypes
{
    public const string Comparison = "comparison";
    public const string Spatial = "spatial";
    public const string Temporal = "temporal";
    public const string Nested = "nested";
}

public sealed record HonuaSavedQueryContent
{
    [JsonPropertyName("naturalLanguageQuery")]
    public string? NaturalLanguageQuery { get; init; }

    [JsonPropertyName("layerId")]
    public int LayerId { get; init; }

    [JsonPropertyName("serviceName")]
    public string? ServiceName { get; init; }

    [JsonPropertyName("filterPlan")]
    public HonuaFilterPlan? FilterPlan { get; init; }

    [JsonPropertyName("outFields")]
    public string[] OutFields { get; init; } = [];

    [JsonPropertyName("outputSrid")]
    public int? OutputSrid { get; init; }

    [JsonPropertyName("previewLimit")]
    public int? PreviewLimit { get; init; }

    [JsonPropertyName("outputFormat")]
    public string? OutputFormat { get; init; }

    [JsonPropertyName("units")]
    public string? Units { get; init; }

    [JsonPropertyName("metadata")]
    public Dictionary<string, string> Metadata { get; init; } = new(StringComparer.Ordinal);
}

public sealed record HonuaFilterPlan
{
    [JsonPropertyName("combinator")]
    public string Combinator { get; init; } = HonuaFilterPlanCombinators.And;

    [JsonPropertyName("clauses")]
    public HonuaFilterPlanClause[] Clauses { get; init; } = [];
}

public sealed record HonuaFilterPlanClause
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = HonuaFilterClauseTypes.Comparison;

    [JsonPropertyName("comparison")]
    public HonuaComparisonClause? Comparison { get; init; }

    [JsonPropertyName("spatial")]
    public HonuaSpatialClause? Spatial { get; init; }

    [JsonPropertyName("temporal")]
    public HonuaTemporalClause? Temporal { get; init; }
}

public sealed record HonuaComparisonClause
{
    [JsonPropertyName("property")]
    public string Property { get; init; } = string.Empty;

    [JsonPropertyName("operator")]
    public string Operator { get; init; } = string.Empty;

    [JsonPropertyName("value")]
    public JsonElement? Value { get; init; }
}

public sealed record HonuaSpatialClause
{
    [JsonPropertyName("operator")]
    public string Operator { get; init; } = string.Empty;

    [JsonPropertyName("geometry")]
    public JsonElement? Geometry { get; init; }

    [JsonPropertyName("distance")]
    public double? Distance { get; init; }

    [JsonPropertyName("distanceUnit")]
    public string? DistanceUnit { get; init; }
}

public sealed record HonuaTemporalClause
{
    [JsonPropertyName("property")]
    public string Property { get; init; } = string.Empty;

    [JsonPropertyName("operator")]
    public string Operator { get; init; } = string.Empty;

    [JsonPropertyName("start")]
    public string? Start { get; init; }

    [JsonPropertyName("end")]
    public string? End { get; init; }
}

public sealed record HonuaAnalysisPackageContent
{
    [JsonPropertyName("intent")]
    public HonuaAnalysisIntent? Intent { get; init; }

    [JsonPropertyName("plan")]
    public HonuaAnalysisPlan Plan { get; init; } = new();

    [JsonPropertyName("parameters")]
    public Dictionary<string, string> Parameters { get; init; } = new(StringComparer.Ordinal);

    [JsonPropertyName("requestedArtifacts")]
    public string[] RequestedArtifacts { get; init; } = [];

    [JsonPropertyName("bindingHints")]
    public HonuaArtifactBindingRef[] BindingHints { get; init; } = [];

    [JsonPropertyName("spatialReferenceId")]
    public int? SpatialReferenceId { get; init; }

    [JsonPropertyName("units")]
    public string? Units { get; init; }

    [JsonPropertyName("metadata")]
    public Dictionary<string, string> Metadata { get; init; } = new(StringComparer.Ordinal);
}

public sealed record HonuaAnalysisIntent
{
    [JsonPropertyName("intentId")]
    public string IntentId { get; init; } = string.Empty;

    [JsonPropertyName("goal")]
    public string Goal { get; init; } = string.Empty;

    [JsonPropertyName("mode")]
    public string? Mode { get; init; }

    [JsonPropertyName("requestedOutputs")]
    public string[] RequestedOutputs { get; init; } = [];

    [JsonPropertyName("inputs")]
    public string[] Inputs { get; init; } = [];
}

public sealed record HonuaAnalysisPlan
{
    [JsonPropertyName("planId")]
    public string PlanId { get; init; } = string.Empty;

    [JsonPropertyName("intentId")]
    public string IntentId { get; init; } = string.Empty;

    [JsonPropertyName("steps")]
    public HonuaAnalysisPlanStep[] Steps { get; init; } = [];

    [JsonPropertyName("outputs")]
    public string[] Outputs { get; init; } = [];

    [JsonPropertyName("warnings")]
    public string[] Warnings { get; init; } = [];
}

public sealed record HonuaAnalysisPlanStep
{
    [JsonPropertyName("stepId")]
    public string StepId { get; init; } = string.Empty;

    [JsonPropertyName("kind")]
    public string Kind { get; init; } = HonuaAnalysisPlanStepKinds.Geoprocess;

    [JsonPropertyName("processId")]
    public string? ProcessId { get; init; }

    [JsonPropertyName("inputs")]
    public Dictionary<string, string> Inputs { get; init; } = new(StringComparer.Ordinal);

    [JsonPropertyName("dependsOn")]
    public string[] DependsOn { get; init; } = [];
}

public sealed record HonuaArtifactBindingRef
{
    [JsonPropertyName("artifactId")]
    public string ArtifactId { get; init; } = string.Empty;

    [JsonPropertyName("sourceItemId")]
    public string? SourceItemId { get; init; }

    [JsonPropertyName("sourceVersion")]
    public int? SourceVersion { get; init; }

    [JsonPropertyName("sourceVersionId")]
    public string? SourceVersionId { get; init; }

    [JsonPropertyName("role")]
    public string Role { get; init; } = "dataSource";

    [JsonPropertyName("targetKind")]
    public string TargetKind { get; init; } = "content";

    [JsonPropertyName("targetSlot")]
    public string TargetSlot { get; init; } = "default";
}

public sealed record HonuaResultArtifactRecord
{
    [JsonPropertyName("artifactId")]
    public string ArtifactId { get; init; } = string.Empty;

    [JsonPropertyName("resultPackageId")]
    public string ResultPackageId { get; init; } = string.Empty;

    [JsonPropertyName("jobId")]
    public string JobId { get; init; } = string.Empty;

    [JsonPropertyName("sourceItemId")]
    public string SourceItemId { get; init; } = string.Empty;

    [JsonPropertyName("sourceVersion")]
    public int SourceVersion { get; init; }

    [JsonPropertyName("kind")]
    public string Kind { get; init; } = HonuaArtifactKinds.FeatureLayer;

    [JsonPropertyName("label")]
    public string Label { get; init; } = string.Empty;

    [JsonPropertyName("uri")]
    public string? Uri { get; init; }

    [JsonPropertyName("contentType")]
    public string? ContentType { get; init; }

    [JsonPropertyName("byteSize")]
    public long? ByteSize { get; init; }

    [JsonPropertyName("retentionState")]
    public string RetentionState { get; init; } = "retained";

    [JsonPropertyName("promotionState")]
    public string PromotionState { get; init; } = "none";

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; init; }

    [JsonPropertyName("expiresAt")]
    public DateTimeOffset? ExpiresAt { get; init; }
}

public sealed record HonuaSavedQueryPreviewResult
{
    [JsonPropertyName("previewArtifactId")]
    public string PreviewArtifactId { get; init; } = string.Empty;

    [JsonPropertyName("itemId")]
    public string ItemId { get; init; } = string.Empty;

    [JsonPropertyName("version")]
    public int Version { get; init; }

    [JsonPropertyName("layerId")]
    public int LayerId { get; init; }

    [JsonPropertyName("features")]
    public HonuaSavedQueryPreviewFeature[] Features { get; init; } = [];

    [JsonPropertyName("totalCount")]
    public long? TotalCount { get; init; }

    [JsonPropertyName("exceededPreviewLimit")]
    public bool ExceededPreviewLimit { get; init; }

    [JsonPropertyName("binding")]
    public HonuaArtifactBindingRef Binding { get; init; } = new();
}

public sealed record HonuaSavedQueryPreviewFeature
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("attributes")]
    public Dictionary<string, JsonElement> Attributes { get; init; } = new(StringComparer.Ordinal);

    [JsonPropertyName("hasGeometry")]
    public bool HasGeometry { get; init; }
}

public sealed record HonuaAnalysisJobFailure
{
    [JsonPropertyName("jobId")]
    public string JobId { get; init; } = string.Empty;

    [JsonPropertyName("classification")]
    public string Classification { get; init; } = "unknown";

    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;

    [JsonPropertyName("isTerminal")]
    public bool IsTerminal { get; init; }

    [JsonPropertyName("failedAt")]
    public DateTimeOffset? FailedAt { get; init; }
}
