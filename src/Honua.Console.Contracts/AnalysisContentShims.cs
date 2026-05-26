using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Honua.Console.Contracts;

// SHIM(honua-sdk-dotnet#170): the honua-server Analysis Content API (#1182 commit 53eacb6d5,
// "Saved query and analysis content versions with job artifacts") is merged to honua-server trunk,
// but honua-sdk-dotnet does not yet project the AnalysisContent / saved-query DTOs (no consumable
// Honua.Sdk AnalysisContent package, and honua-console wires no SDK NuGet feed). Per SDK_SHIM_POLICY.md
// the wire records and the thin HTTP client live behind this single Console contracts boundary until
// honua-sdk-dotnet publishes the projection and honua-console swaps to SDK types. Do not add a
// sibling-repo ProjectReference; do not mirror these DTOs anywhere else. The wire shape mirrors
// honua-server/src/Honua.Core/Features/AnalysisContent/Domain/AnalysisContentContracts.cs and
// honua-server/src/Honua.Core/Features/NlQuery/Domain/FilterPlan.cs (camelCase, string enums via
// [JsonStringEnumMemberName], null props omitted). Unlike the Studio/Operate shims, these endpoints
// return the response DTO BARE (no {success,data} envelope) and signal failure with
// application/problem+json, so SendAsync deserializes the response type directly. This slice covers the
// savedQuery family only; analysis-package runs/reruns/jobs are an analysisPackage concern
// (/studio/analysis) and are intentionally out of scope here. See SDK_SHIM_POLICY "Active Shims".

#region Enums (AnalysisContentContracts.cs / FilterPlan.cs)

[JsonConverter(typeof(JsonStringEnumConverter<AnalysisContentKind>))]
public enum AnalysisContentKind
{
    [JsonStringEnumMemberName("savedQuery")] SavedQuery,
    [JsonStringEnumMemberName("analysisPackage")] AnalysisPackage
}

[JsonConverter(typeof(JsonStringEnumConverter<AnalysisContentLifecycle>))]
public enum AnalysisContentLifecycle
{
    [JsonStringEnumMemberName("active")] Active,
    [JsonStringEnumMemberName("archived")] Archived,
    [JsonStringEnumMemberName("deleted")] Deleted
}

[JsonConverter(typeof(JsonStringEnumConverter<FilterPlanCombinator>))]
public enum FilterPlanCombinator
{
    [JsonStringEnumMemberName("and")] And,
    [JsonStringEnumMemberName("or")] Or
}

#endregion

#region FilterPlan tree (FilterPlan.cs)

// The server's FilterPlan clause hierarchy is discriminated by a string `type` property naming exactly
// one populated sibling object (comparison/spatial/temporal/nested) - NOT a System.Text.Json
// polymorphic $type tag. Clause operators are free strings (eq/ne/lt/le/gt/ge/like/in, intersects/
// within/contains/dwithin, before/after/during), not enums, so they are modelled as plain strings.

public sealed record FilterPlan
{
    [JsonPropertyName("combinator")] public FilterPlanCombinator Combinator { get; init; }
    [JsonPropertyName("clauses")] public IReadOnlyList<FilterPlanClause> Clauses { get; init; } = [];
}

public sealed record FilterPlanClause
{
    [JsonPropertyName("type")] public string Type { get; init; } = string.Empty;
    [JsonPropertyName("comparison")] public ComparisonClause? Comparison { get; init; }
    [JsonPropertyName("spatial")] public SpatialClause? Spatial { get; init; }
    [JsonPropertyName("temporal")] public TemporalClause? Temporal { get; init; }
    [JsonPropertyName("nested")] public NestedClause? Nested { get; init; }
}

public sealed record ComparisonClause
{
    [JsonPropertyName("property")] public string Property { get; init; } = string.Empty;
    [JsonPropertyName("operator")] public string Operator { get; init; } = string.Empty;

    // string | number | bool | string[]; round-trips as a JsonElement when read back from the server.
    [JsonPropertyName("value")] public object? Value { get; init; }
}

public sealed record SpatialClause
{
    [JsonPropertyName("operator")] public string Operator { get; init; } = string.Empty;
    [JsonPropertyName("geometry")] public JsonElement? Geometry { get; init; }
    [JsonPropertyName("distance")] public double? Distance { get; init; }
    [JsonPropertyName("distanceUnit")] public string? DistanceUnit { get; init; }
}

public sealed record TemporalClause
{
    [JsonPropertyName("property")] public string Property { get; init; } = string.Empty;
    [JsonPropertyName("operator")] public string Operator { get; init; } = string.Empty;
    [JsonPropertyName("start")] public string? Start { get; init; }
    [JsonPropertyName("end")] public string? End { get; init; }
}

public sealed record NestedClause
{
    [JsonPropertyName("combinator")] public FilterPlanCombinator Combinator { get; init; }
    [JsonPropertyName("clauses")] public IReadOnlyList<FilterPlanClause> Clauses { get; init; } = [];
}

#endregion

#region Domain DTOs (AnalysisContentContracts.cs)

public sealed record SavedQueryContent
{
    [JsonPropertyName("naturalLanguageQuery")] public string? NaturalLanguageQuery { get; init; }
    [JsonPropertyName("layerId")] public int LayerId { get; init; }
    [JsonPropertyName("serviceName")] public string? ServiceName { get; init; }
    [JsonPropertyName("filterPlan")] public FilterPlan? FilterPlan { get; init; }
    [JsonPropertyName("outFields")] public IReadOnlyList<string> OutFields { get; init; } = [];
    [JsonPropertyName("outputSrid")] public int? OutputSrid { get; init; }
    [JsonPropertyName("previewLimit")] public int? PreviewLimit { get; init; }
    [JsonPropertyName("outputFormat")] public string? OutputFormat { get; init; }
    [JsonPropertyName("units")] public string? Units { get; init; }
    [JsonPropertyName("metadata")] public IReadOnlyDictionary<string, string> Metadata { get; init; } =
        new Dictionary<string, string>();
}

public sealed record AnalysisContentItem
{
    [JsonPropertyName("itemId")] public string ItemId { get; init; } = string.Empty;
    [JsonPropertyName("kind")] public AnalysisContentKind Kind { get; init; }
    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
    [JsonPropertyName("title")] public string? Title { get; init; }
    [JsonPropertyName("ownerId")] public string? OwnerId { get; init; }
    [JsonPropertyName("visibility")] public string Visibility { get; init; } = "organization";
    [JsonPropertyName("currentVersion")] public int CurrentVersion { get; init; }
    [JsonPropertyName("currentVersionId")] public string CurrentVersionId { get; init; } = string.Empty;
    [JsonPropertyName("lifecycle")] public AnalysisContentLifecycle Lifecycle { get; init; }
    [JsonPropertyName("createdAt")] public DateTimeOffset CreatedAt { get; init; }
    [JsonPropertyName("updatedAt")] public DateTimeOffset UpdatedAt { get; init; }
    [JsonPropertyName("createdBy")] public string? CreatedBy { get; init; }
}

public sealed record AnalysisContentVersion
{
    [JsonPropertyName("versionId")] public string VersionId { get; init; } = string.Empty;
    [JsonPropertyName("itemId")] public string ItemId { get; init; } = string.Empty;
    [JsonPropertyName("version")] public int Version { get; init; }
    [JsonPropertyName("kind")] public AnalysisContentKind Kind { get; init; }
    [JsonPropertyName("savedQuery")] public SavedQueryContent? SavedQuery { get; init; }
    [JsonPropertyName("contentHash")] public string ContentHash { get; init; } = string.Empty;
    [JsonPropertyName("basedOnVersionId")] public string? BasedOnVersionId { get; init; }
    [JsonPropertyName("createdFromJobId")] public string? CreatedFromJobId { get; init; }
    [JsonPropertyName("createdFromArtifactIds")] public IReadOnlyList<string> CreatedFromArtifactIds { get; init; } = [];
    [JsonPropertyName("createdAt")] public DateTimeOffset CreatedAt { get; init; }
    [JsonPropertyName("createdBy")] public string? CreatedBy { get; init; }
}

public sealed record ArtifactBindingRef
{
    [JsonPropertyName("artifactId")] public string ArtifactId { get; init; } = string.Empty;
    [JsonPropertyName("sourceItemId")] public string? SourceItemId { get; init; }
    [JsonPropertyName("sourceVersion")] public int? SourceVersion { get; init; }
    [JsonPropertyName("sourceVersionId")] public string? SourceVersionId { get; init; }
    [JsonPropertyName("role")] public string Role { get; init; } = "dataSource";
    [JsonPropertyName("targetKind")] public string TargetKind { get; init; } = "content";
    [JsonPropertyName("targetSlot")] public string TargetSlot { get; init; } = "default";
}

// Retention/promotion/kind are surfaced as strings: the client only displays them, and the server
// serialises ArtifactKind as PascalCase while retention/promotion are lowercased tokens. Keeping them
// as strings avoids pulling the whole geoprocessing enum surface into the saved-query shim.
public sealed record ResultArtifactRecord
{
    [JsonPropertyName("artifactId")] public string ArtifactId { get; init; } = string.Empty;
    [JsonPropertyName("resultPackageId")] public string ResultPackageId { get; init; } = string.Empty;
    [JsonPropertyName("jobId")] public string JobId { get; init; } = string.Empty;
    [JsonPropertyName("sourceItemId")] public string SourceItemId { get; init; } = string.Empty;
    [JsonPropertyName("sourceVersion")] public int SourceVersion { get; init; }
    [JsonPropertyName("sourceVersionId")] public string SourceVersionId { get; init; } = string.Empty;
    [JsonPropertyName("kind")] public string Kind { get; init; } = string.Empty;
    [JsonPropertyName("label")] public string Label { get; init; } = string.Empty;
    [JsonPropertyName("uri")] public string? Uri { get; init; }
    [JsonPropertyName("contentType")] public string? ContentType { get; init; }
    [JsonPropertyName("retentionState")] public string? RetentionState { get; init; }
    [JsonPropertyName("promotionState")] public string? PromotionState { get; init; }
    [JsonPropertyName("createdAt")] public DateTimeOffset CreatedAt { get; init; }
    [JsonPropertyName("expiresAt")] public DateTimeOffset? ExpiresAt { get; init; }
}

public sealed record SavedQueryPreviewFeature
{
    [JsonPropertyName("id")] public long Id { get; init; }
    [JsonPropertyName("attributes")] public IReadOnlyDictionary<string, JsonElement> Attributes { get; init; } =
        new Dictionary<string, JsonElement>();
    [JsonPropertyName("hasGeometry")] public bool HasGeometry { get; init; }
}

public sealed record SavedQueryPreviewResult
{
    [JsonPropertyName("previewArtifactId")] public string PreviewArtifactId { get; init; } = string.Empty;
    [JsonPropertyName("itemId")] public string ItemId { get; init; } = string.Empty;
    [JsonPropertyName("version")] public int Version { get; init; }
    [JsonPropertyName("layerId")] public int LayerId { get; init; }
    [JsonPropertyName("features")] public IReadOnlyList<SavedQueryPreviewFeature> Features { get; init; } = [];
    [JsonPropertyName("totalCount")] public long? TotalCount { get; init; }
    [JsonPropertyName("exceededPreviewLimit")] public bool ExceededPreviewLimit { get; init; }
    [JsonPropertyName("binding")] public ArtifactBindingRef Binding { get; init; } = new();
}

#endregion

#region Endpoint envelopes (AnalysisContentApiModels.cs)

public sealed record AnalysisContentItemResponse
{
    [JsonPropertyName("item")] public AnalysisContentItem Item { get; init; } = new();
    [JsonPropertyName("version")] public AnalysisContentVersion Version { get; init; } = new();
}

public sealed record AnalysisContentVersionResponse
{
    [JsonPropertyName("item")] public AnalysisContentItem Item { get; init; } = new();
    [JsonPropertyName("version")] public AnalysisContentVersion Version { get; init; } = new();
}

public sealed record AnalysisArtifactResponse
{
    [JsonPropertyName("artifact")] public ResultArtifactRecord Artifact { get; init; } = new();
    [JsonPropertyName("binding")] public ArtifactBindingRef Binding { get; init; } = new();
}

#endregion

#region Request bodies (AnalysisContentApiModels.cs)

public sealed record CreateAnalysisContentItemRequest
{
    [JsonPropertyName("kind")] public AnalysisContentKind Kind { get; init; } = AnalysisContentKind.SavedQuery;
    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
    [JsonPropertyName("title")] public string? Title { get; init; }
    [JsonPropertyName("savedQuery")] public SavedQueryContent? SavedQuery { get; init; }
}

public sealed record CreateAnalysisContentVersionRequest
{
    [JsonPropertyName("savedQuery")] public SavedQueryContent? SavedQuery { get; init; }
    [JsonPropertyName("basedOnVersionId")] public string? BasedOnVersionId { get; init; }
}

public sealed record PreviewSavedQueryRequest
{
    [JsonPropertyName("limit")] public int? Limit { get; init; }
}

#endregion

#region Result types

public sealed record AnalysisContentEndpointResult<T>(T? Data, AnalysisContentEndpointIssue? Issue)
{
    public bool IsSuccess => Issue is null;

    public static AnalysisContentEndpointResult<T> FromData(T data) => new(data, null);

    public static AnalysisContentEndpointResult<T> FromIssue(AnalysisContentEndpointIssue issue) => new(default, issue);
}

public sealed record AnalysisContentEndpointIssue(
    string State,
    string Contract,
    string Detail,
    int? StatusCode = null)
{
    /// <summary>True when the server reported a version conflict and the caller must reopen-latest-and-rebase.</summary>
    public bool IsConflict => StatusCode == (int)HttpStatusCode.Conflict;
}

#endregion

#region Typed client

public sealed record AnalysisContentClientOptions(Uri BaseUri, string? ApiKey = null);

public interface IAnalysisContentClient
{
    Uri BaseUri { get; }

    Task<AnalysisContentEndpointResult<AnalysisContentItemResponse>> CreateSavedQueryItemAsync(
        CreateAnalysisContentItemRequest request,
        CancellationToken cancellationToken = default);

    Task<AnalysisContentEndpointResult<AnalysisContentItemResponse>> GetItemAsync(
        string itemId,
        CancellationToken cancellationToken = default);

    Task<AnalysisContentEndpointResult<AnalysisContentVersionResponse>> GetLatestVersionAsync(
        string itemId,
        CancellationToken cancellationToken = default);

    Task<AnalysisContentEndpointResult<AnalysisContentVersionResponse>> GetVersionAsync(
        string itemId,
        int contentVersion,
        CancellationToken cancellationToken = default);

    Task<AnalysisContentEndpointResult<AnalysisContentVersionResponse>> AddVersionAsync(
        string itemId,
        CreateAnalysisContentVersionRequest request,
        CancellationToken cancellationToken = default);

    Task<AnalysisContentEndpointResult<SavedQueryPreviewResult>> PreviewSavedQueryAsync(
        string itemId,
        int contentVersion,
        PreviewSavedQueryRequest request,
        CancellationToken cancellationToken = default);

    Task<AnalysisContentEndpointResult<AnalysisArtifactResponse>> GetArtifactAsync(
        string artifactId,
        CancellationToken cancellationToken = default);
}

public sealed class AnalysisContentHttpClient : IAnalysisContentClient, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly string? _apiKey;

    public AnalysisContentHttpClient(HttpClient httpClient, AnalysisContentClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);

        BaseUri = options.BaseUri;
        _apiKey = options.ApiKey;
        _httpClient = httpClient;
        _httpClient.BaseAddress ??= BaseUri;
    }

    public Uri BaseUri { get; }

    public Task<AnalysisContentEndpointResult<AnalysisContentItemResponse>> CreateSavedQueryItemAsync(
        CreateAnalysisContentItemRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SendAsync<CreateAnalysisContentItemRequest, AnalysisContentItemResponse>(
            HttpMethod.Post,
            "/api/v1/analysis/content/items",
            request,
            "POST /api/v1/analysis/content/items",
            cancellationToken);
    }

    public Task<AnalysisContentEndpointResult<AnalysisContentItemResponse>> GetItemAsync(
        string itemId,
        CancellationToken cancellationToken = default) =>
        SendAsync<object, AnalysisContentItemResponse>(
            HttpMethod.Get,
            $"/api/v1/analysis/content/items/{Uri.EscapeDataString(itemId)}",
            null,
            "GET /api/v1/analysis/content/items/{itemId}",
            cancellationToken);

    public Task<AnalysisContentEndpointResult<AnalysisContentVersionResponse>> GetLatestVersionAsync(
        string itemId,
        CancellationToken cancellationToken = default) =>
        SendAsync<object, AnalysisContentVersionResponse>(
            HttpMethod.Get,
            $"/api/v1/analysis/content/items/{Uri.EscapeDataString(itemId)}/versions/latest",
            null,
            "GET /api/v1/analysis/content/items/{itemId}/versions/latest",
            cancellationToken);

    public Task<AnalysisContentEndpointResult<AnalysisContentVersionResponse>> GetVersionAsync(
        string itemId,
        int contentVersion,
        CancellationToken cancellationToken = default) =>
        SendAsync<object, AnalysisContentVersionResponse>(
            HttpMethod.Get,
            $"/api/v1/analysis/content/items/{Uri.EscapeDataString(itemId)}/versions/{contentVersion.ToString(CultureInfo.InvariantCulture)}",
            null,
            "GET /api/v1/analysis/content/items/{itemId}/versions/{contentVersion}",
            cancellationToken);

    public Task<AnalysisContentEndpointResult<AnalysisContentVersionResponse>> AddVersionAsync(
        string itemId,
        CreateAnalysisContentVersionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SendAsync<CreateAnalysisContentVersionRequest, AnalysisContentVersionResponse>(
            HttpMethod.Post,
            $"/api/v1/analysis/content/items/{Uri.EscapeDataString(itemId)}/versions",
            request,
            "POST /api/v1/analysis/content/items/{itemId}/versions",
            cancellationToken);
    }

    public Task<AnalysisContentEndpointResult<SavedQueryPreviewResult>> PreviewSavedQueryAsync(
        string itemId,
        int contentVersion,
        PreviewSavedQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SendAsync<PreviewSavedQueryRequest, SavedQueryPreviewResult>(
            HttpMethod.Post,
            $"/api/v1/analysis/content/items/{Uri.EscapeDataString(itemId)}/versions/{contentVersion.ToString(CultureInfo.InvariantCulture)}/preview",
            request,
            "POST /api/v1/analysis/content/items/{itemId}/versions/{contentVersion}/preview",
            cancellationToken);
    }

    public Task<AnalysisContentEndpointResult<AnalysisArtifactResponse>> GetArtifactAsync(
        string artifactId,
        CancellationToken cancellationToken = default) =>
        SendAsync<object, AnalysisArtifactResponse>(
            HttpMethod.Get,
            $"/api/v1/analysis/artifacts/{Uri.EscapeDataString(artifactId)}",
            null,
            "GET /api/v1/analysis/artifacts/{artifactId}",
            cancellationToken);

    public void Dispose() => _httpClient.Dispose();

    private async Task<AnalysisContentEndpointResult<TResponse>> SendAsync<TBody, TResponse>(
        HttpMethod method,
        string path,
        TBody? body,
        string contract,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        if (!string.IsNullOrWhiteSpace(_apiKey))
        {
            request.Headers.TryAddWithoutValidation("X-API-Key", _apiKey);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType(), options: JsonOptions);
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
            return Unavailable<TResponse>(contract, ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            return Unavailable<TResponse>(contract, ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var detail = await ReadProblemDetailAsync(response, cancellationToken).ConfigureAwait(false);
                return AnalysisContentEndpointResult<TResponse>.FromIssue(
                    CreateIssue(contract, response.StatusCode, detail));
            }

            TResponse? payload;
            try
            {
                // The Analysis Content API returns the response DTO bare (no {success,data} envelope).
                payload = await response.Content
                    .ReadFromJsonAsync<TResponse>(JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (JsonException ex)
            {
                return AnalysisContentEndpointResult<TResponse>.FromIssue(new AnalysisContentEndpointIssue(
                    "Unsupported",
                    contract,
                    $"The Honua server analysis-content response did not match the expected API shape: {ex.Message}",
                    (int)response.StatusCode));
            }

            if (payload is not null)
            {
                return AnalysisContentEndpointResult<TResponse>.FromData(payload);
            }

            return AnalysisContentEndpointResult<TResponse>.FromIssue(new AnalysisContentEndpointIssue(
                "Unavailable",
                contract,
                "The Honua server analysis-content response did not include a body.",
                (int)response.StatusCode));
        }
    }

    private static AnalysisContentEndpointResult<TResponse> Unavailable<TResponse>(string contract, Exception ex) =>
        AnalysisContentEndpointResult<TResponse>.FromIssue(new AnalysisContentEndpointIssue(
            "Unavailable",
            contract,
            $"The Honua server analysis-content endpoint could not be reached: {ex.Message}"));

    private static async Task<string?> ReadProblemDetailAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        // Errors are application/problem+json; surface the server's detail when present, best-effort.
        try
        {
            var problem = await response.Content
                .ReadFromJsonAsync<ProblemDetailBody>(JsonOptions, cancellationToken)
                .ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(problem?.Detail) ? null : problem!.Detail;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or HttpRequestException or InvalidOperationException)
        {
            return null;
        }
    }

    private static AnalysisContentEndpointIssue CreateIssue(string contract, HttpStatusCode statusCode, string? serverDetail)
    {
        var state = statusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "Missing permission",
            HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotImplemented => "Unsupported",
            HttpStatusCode.Conflict => "Conflict",
            _ => "Unavailable"
        };

        var detail = statusCode switch
        {
            HttpStatusCode.Unauthorized => "The Honua server rejected the analysis-content request because admin authentication is missing.",
            HttpStatusCode.Forbidden => "The Honua server rejected the analysis-content request because the current principal lacks admin permission.",
            HttpStatusCode.NotFound => "The Honua server does not expose this analysis-content contract, or the item/version was not found.",
            HttpStatusCode.MethodNotAllowed => "The Honua server exposes the route but not the required analysis-content API verb.",
            HttpStatusCode.NotImplemented => "The Honua server reports this analysis-content capability is not implemented.",
            HttpStatusCode.Conflict => "The saved-query item changed on the server; reopen the latest version before retrying.",
            HttpStatusCode.BadRequest => "The Honua server rejected the saved-query request as invalid; check the source binding and predicates.",
            _ => string.Format(
                CultureInfo.InvariantCulture,
                "The Honua server returned HTTP {0} ({1}) for the analysis-content request.",
                (int)statusCode,
                statusCode)
        };

        if (!string.IsNullOrWhiteSpace(serverDetail))
        {
            detail = $"{detail} {serverDetail}";
        }

        return new AnalysisContentEndpointIssue(state, contract, detail, (int)statusCode);
    }

    private sealed record ProblemDetailBody
    {
        [JsonPropertyName("title")] public string? Title { get; init; }
        [JsonPropertyName("detail")] public string? Detail { get; init; }
        [JsonPropertyName("status")] public int? Status { get; init; }
    }
}

#endregion
