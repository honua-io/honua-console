using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Honua.Console.Contracts;

// SHIM(honua-sdk-dotnet#166): the honua-server Analysis Content API (#1182 commit 53eacb6d5 —
// "Saved query and analysis content versions with job artifacts") is merged to honua-server trunk and
// exposes saved-query/analysis-package content versions, saved-query preview, analysis-package job
// submit/rerun, artifact resolution, and bounded job diagnostics under /api/v1/analysis/**. honua-sdk-dotnet
// does not project these Analysis Content DTOs (no consumable Honua.Sdk analysis package; honua-console
// wires no SDK NuGet feed), so per SDK_SHIM_POLICY.md the wire records and the thin HTTP client live behind
// this single Console contracts boundary until the SDK publishes the analysis projection and honua-console
// swaps to SDK types. Do not add a sibling-repo ProjectReference; do not mirror these DTOs elsewhere.
//
// Wire shape mirrors honua-server source exactly (verified against trunk):
//   * Honua.Core/Features/AnalysisContent/Domain/AnalysisContentContracts.cs
//   * Honua.Core/Features/Geoprocessing/Domain/AnalysisPlan.cs + AnalysisIntent.cs + GeoprocessingEnums.cs
//   * Honua.Server/Features/AnalysisContent/AnalysisContentApiModels.cs (response wrappers, request bodies)
// Serialization is plain camelCase JSON (NOT the ApiResponse<T> envelope the Studio lifecycle uses); null
// props omitted; enums string-serialized. Enum string values match the server: AnalysisContentKind /
// AnalysisContentLifecycle / AnalysisJobFailureClassification carry explicit camelCase member names, while
// ArtifactKind / AnalysisPlanStepKind / ExecutionJobStatus / ExecutionLogLevel serialize as their PascalCase
// member names (no member-name attribute on the server). See docs/admin-api/analysis-content.md.

#region Enums

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

// PascalCase wire values (server enum has no [JsonStringEnumMemberName]).
[JsonConverter(typeof(JsonStringEnumConverter<ArtifactKind>))]
public enum ArtifactKind
{
    Scalar,
    FeatureLayer,
    Table,
    Raster,
    File,
    Report,
    Map,
    AppBundle
}

// PascalCase wire values (server enum has no [JsonStringEnumMemberName]).
[JsonConverter(typeof(JsonStringEnumConverter<AnalysisPlanStepKind>))]
public enum AnalysisPlanStepKind
{
    QueryFeatures,
    Geoprocess,
    Aggregate,
    RenderMap,
    Export
}

// PascalCase wire values (server enum has no [JsonStringEnumMemberName]).
[JsonConverter(typeof(JsonStringEnumConverter<ExecutionJobStatus>))]
public enum ExecutionJobStatus
{
    Queued,
    Provisioning,
    Running,
    Succeeded,
    Failed,
    Cancelled
}

// PascalCase wire values (server enum has no [JsonStringEnumMemberName]).
[JsonConverter(typeof(JsonStringEnumConverter<ExecutionLogLevel>))]
public enum ExecutionLogLevel
{
    Trace,
    Debug,
    Information,
    Warning,
    Error,
    Critical
}

[JsonConverter(typeof(JsonStringEnumConverter<AnalysisJobFailureClassification>))]
public enum AnalysisJobFailureClassification
{
    [JsonStringEnumMemberName("validationFailed")] ValidationFailed,
    [JsonStringEnumMemberName("authorizationDenied")] AuthorizationDenied,
    [JsonStringEnumMemberName("cancelled")] Cancelled,
    [JsonStringEnumMemberName("timedOut")] TimedOut,
    [JsonStringEnumMemberName("artifactOutputFailed")] ArtifactOutputFailed,
    [JsonStringEnumMemberName("executionFailed")] ExecutionFailed,
    [JsonStringEnumMemberName("storeUnavailable")] StoreUnavailable,
    [JsonStringEnumMemberName("unknown")] Unknown
}

#endregion

#region Domain DTOs (AnalysisContentContracts.cs / AnalysisPlan.cs / AnalysisIntent.cs)

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
    [JsonPropertyName("lifecycle")] public AnalysisContentLifecycle Lifecycle { get; init; } = AnalysisContentLifecycle.Active;
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
    [JsonPropertyName("analysisPackage")] public AnalysisPackageContent? AnalysisPackage { get; init; }
    [JsonPropertyName("contentHash")] public string ContentHash { get; init; } = string.Empty;
    [JsonPropertyName("basedOnVersionId")] public string? BasedOnVersionId { get; init; }
    [JsonPropertyName("createdFromJobId")] public string? CreatedFromJobId { get; init; }
    [JsonPropertyName("createdFromArtifactIds")] public IReadOnlyList<string> CreatedFromArtifactIds { get; init; } = [];
    [JsonPropertyName("createdAt")] public DateTimeOffset CreatedAt { get; init; }
    [JsonPropertyName("createdBy")] public string? CreatedBy { get; init; }
}

public sealed record SavedQueryContent
{
    [JsonPropertyName("naturalLanguageQuery")] public string? NaturalLanguageQuery { get; init; }
    [JsonPropertyName("layerId")] public int LayerId { get; init; }
    [JsonPropertyName("serviceName")] public string? ServiceName { get; init; }
    // FilterPlan is opaque to Console; the saved plan is server-owned and shown as raw JSON only.
    [JsonPropertyName("filterPlan")] public JsonElement? FilterPlan { get; init; }
    [JsonPropertyName("outFields")] public IReadOnlyList<string> OutFields { get; init; } = [];
    [JsonPropertyName("outputSrid")] public int? OutputSrid { get; init; }
    [JsonPropertyName("previewLimit")] public int? PreviewLimit { get; init; }
    [JsonPropertyName("outputFormat")] public string? OutputFormat { get; init; }
    [JsonPropertyName("units")] public string? Units { get; init; }
    [JsonPropertyName("metadata")] public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}

public sealed record AnalysisPackageContent
{
    [JsonPropertyName("intent")] public AnalysisIntent? Intent { get; init; }
    [JsonPropertyName("plan")] public AnalysisPlan Plan { get; init; } = new();
    [JsonPropertyName("parameters")] public IReadOnlyDictionary<string, string> Parameters { get; init; } = new Dictionary<string, string>();
    [JsonPropertyName("requestedArtifacts")] public IReadOnlyList<ArtifactKind> RequestedArtifacts { get; init; } = [];
    [JsonPropertyName("bindingHints")] public IReadOnlyList<ArtifactBindingRef> BindingHints { get; init; } = [];
    [JsonPropertyName("spatialReferenceId")] public int? SpatialReferenceId { get; init; }
    [JsonPropertyName("units")] public string? Units { get; init; }
    [JsonPropertyName("metadata")] public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}

public sealed record AnalysisPlan
{
    [JsonPropertyName("planId")] public string PlanId { get; init; } = string.Empty;
    [JsonPropertyName("intentId")] public string IntentId { get; init; } = string.Empty;
    [JsonPropertyName("steps")] public IReadOnlyList<AnalysisPlanStep> Steps { get; init; } = [];
    [JsonPropertyName("outputs")] public IReadOnlyList<ArtifactKind> Outputs { get; init; } = [];
    [JsonPropertyName("warnings")] public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed record AnalysisPlanStep
{
    [JsonPropertyName("stepId")] public string StepId { get; init; } = string.Empty;
    [JsonPropertyName("kind")] public AnalysisPlanStepKind Kind { get; init; }
    [JsonPropertyName("processId")] public string? ProcessId { get; init; }
    [JsonPropertyName("inputs")] public IReadOnlyDictionary<string, string> Inputs { get; init; } = new Dictionary<string, string>();
    [JsonPropertyName("dependsOn")] public IReadOnlyList<string> DependsOn { get; init; } = [];
}

public sealed record AnalysisIntent
{
    [JsonPropertyName("intentId")] public string IntentId { get; init; } = string.Empty;
    [JsonPropertyName("goal")] public string Goal { get; init; } = string.Empty;
    [JsonPropertyName("mode")] public string? Mode { get; init; }
    [JsonPropertyName("requestedOutputs")] public IReadOnlyList<ArtifactKind> RequestedOutputs { get; init; } = [];
    // IntentConstraints is opaque to Console; carried for round-trips only.
    [JsonPropertyName("constraints")] public JsonElement? Constraints { get; init; }
    [JsonPropertyName("inputs")] public IReadOnlyList<string> Inputs { get; init; } = [];
    [JsonPropertyName("assumptionPolicy")] public string? AssumptionPolicy { get; init; }
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

public sealed record ResultArtifactRecord
{
    [JsonPropertyName("artifactId")] public string ArtifactId { get; init; } = string.Empty;
    [JsonPropertyName("resultPackageId")] public string ResultPackageId { get; init; } = string.Empty;
    [JsonPropertyName("jobId")] public string JobId { get; init; } = string.Empty;
    [JsonPropertyName("sourceItemId")] public string SourceItemId { get; init; } = string.Empty;
    [JsonPropertyName("sourceVersion")] public int SourceVersion { get; init; }
    [JsonPropertyName("sourceVersionId")] public string SourceVersionId { get; init; } = string.Empty;
    [JsonPropertyName("kind")] public ArtifactKind Kind { get; init; }
    [JsonPropertyName("label")] public string Label { get; init; } = string.Empty;
    [JsonPropertyName("uri")] public string? Uri { get; init; }
    [JsonPropertyName("contentType")] public string? ContentType { get; init; }
    [JsonPropertyName("byteSize")] public long? ByteSize { get; init; }
    [JsonPropertyName("metadata")] public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
    [JsonPropertyName("provenance")] public IReadOnlyDictionary<string, string> Provenance { get; init; } = new Dictionary<string, string>();
    // retentionState / promotionState are low-importance display strings; modelled as string to avoid
    // mirroring two more server enums that the builder never branches on.
    [JsonPropertyName("retentionState")] public string? RetentionState { get; init; }
    [JsonPropertyName("promotionState")] public string? PromotionState { get; init; }
    [JsonPropertyName("createdAt")] public DateTimeOffset CreatedAt { get; init; }
    [JsonPropertyName("expiresAt")] public DateTimeOffset? ExpiresAt { get; init; }
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

public sealed record SavedQueryPreviewFeature
{
    [JsonPropertyName("id")] public long Id { get; init; }
    [JsonPropertyName("attributes")] public IReadOnlyDictionary<string, JsonElement> Attributes { get; init; } = new Dictionary<string, JsonElement>();
    [JsonPropertyName("hasGeometry")] public bool HasGeometry { get; init; }
}

public sealed record AnalysisJobLogs
{
    [JsonPropertyName("jobId")] public string JobId { get; init; } = string.Empty;
    [JsonPropertyName("entries")] public IReadOnlyList<AnalysisJobLogEntry> Entries { get; init; } = [];
    [JsonPropertyName("totalCount")] public int TotalCount { get; init; }
    [JsonPropertyName("truncated")] public bool Truncated { get; init; }
}

public sealed record AnalysisJobLogEntry
{
    [JsonPropertyName("timestamp")] public DateTimeOffset Timestamp { get; init; }
    [JsonPropertyName("level")] public ExecutionLogLevel Level { get; init; }
    [JsonPropertyName("message")] public string Message { get; init; } = string.Empty;
    [JsonPropertyName("phase")] public string? Phase { get; init; }
    [JsonPropertyName("metadata")] public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}

public sealed record AnalysisJobFailure
{
    [JsonPropertyName("jobId")] public string JobId { get; init; } = string.Empty;
    [JsonPropertyName("classification")] public AnalysisJobFailureClassification Classification { get; init; }
    [JsonPropertyName("message")] public string Message { get; init; } = string.Empty;
    [JsonPropertyName("isTerminal")] public bool IsTerminal { get; init; }
    [JsonPropertyName("failedAt")] public DateTimeOffset? FailedAt { get; init; }
}

#endregion

#region Response wrappers + request bodies (AnalysisContentApiModels.cs)

// AnalysisContentItemResponse / AnalysisContentVersionResponse share the same { item, version } wire shape;
// Console reads both through this one snapshot record.
public sealed record AnalysisContentSnapshot
{
    [JsonPropertyName("item")] public AnalysisContentItem Item { get; init; } = new();
    [JsonPropertyName("version")] public AnalysisContentVersion Version { get; init; } = new();
}

// AnalysisArtifactResponse: { artifact, binding }.
public sealed record AnalysisArtifactResolution
{
    [JsonPropertyName("artifact")] public ResultArtifactRecord Artifact { get; init; } = new();
    [JsonPropertyName("binding")] public ArtifactBindingRef Binding { get; init; } = new();
}

public sealed record AnalysisContentJobResponse
{
    [JsonPropertyName("jobId")] public string JobId { get; init; } = string.Empty;
    [JsonPropertyName("status")] public ExecutionJobStatus Status { get; init; }
    [JsonPropertyName("version")] public AnalysisContentVersion Version { get; init; } = new();
}

public sealed record CreateAnalysisContentItemRequest
{
    [JsonPropertyName("kind")] public AnalysisContentKind Kind { get; init; }
    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
    [JsonPropertyName("title")] public string? Title { get; init; }
    [JsonPropertyName("savedQuery")] public SavedQueryContent? SavedQuery { get; init; }
    [JsonPropertyName("analysisPackage")] public AnalysisPackageContent? AnalysisPackage { get; init; }
}

public sealed record CreateAnalysisContentVersionRequest
{
    [JsonPropertyName("savedQuery")] public SavedQueryContent? SavedQuery { get; init; }
    [JsonPropertyName("analysisPackage")] public AnalysisPackageContent? AnalysisPackage { get; init; }
    [JsonPropertyName("basedOnVersionId")] public string? BasedOnVersionId { get; init; }
    [JsonPropertyName("createdFromJobId")] public string? CreatedFromJobId { get; init; }
    [JsonPropertyName("createdFromArtifactIds")] public IReadOnlyList<string>? CreatedFromArtifactIds { get; init; }
}

public sealed record PreviewSavedQueryRequest
{
    [JsonPropertyName("limit")] public int? Limit { get; init; }
}

public sealed record RunAnalysisContentVersionRequest
{
    [JsonPropertyName("idempotencyKey")] public string? IdempotencyKey { get; init; }
    [JsonPropertyName("parameters")] public IReadOnlyDictionary<string, string>? Parameters { get; init; }
}

public sealed record RerunAnalysisContentVersionRequest
{
    [JsonPropertyName("idempotencyKey")] public string? IdempotencyKey { get; init; }
    [JsonPropertyName("rerunOfJobId")] public string? RerunOfJobId { get; init; }
    [JsonPropertyName("rerunOfResultPackageId")] public string? RerunOfResultPackageId { get; init; }
    [JsonPropertyName("parameterOverrides")] public IReadOnlyDictionary<string, string>? ParameterOverrides { get; init; }
}

#endregion

#region Result types

public sealed record AnalysisEndpointResult<T>(T? Data, AnalysisEndpointIssue? Issue)
{
    public bool IsSuccess => Issue is null;

    public static AnalysisEndpointResult<T> FromData(T data) => new(data, null);

    public static AnalysisEndpointResult<T> FromIssue(AnalysisEndpointIssue issue) => new(default, issue);
}

public sealed record AnalysisEndpointIssue(
    string State,
    string Contract,
    string Detail,
    int? StatusCode = null)
{
    /// <summary>True when the server reported an optimistic-concurrency conflict and the caller must reload.</summary>
    public bool IsConflict => StatusCode == (int)HttpStatusCode.Conflict;
}

#endregion

#region Typed client

public sealed record StudioAnalysisContentClientOptions(Uri BaseUri, string? ApiKey = null);

/// <summary>
/// Thin honua-console client for the honua-server Analysis Content API (#1182). Backs the Studio analysis
/// builder at <c>/studio/analysis</c>. Every call returns an <see cref="AnalysisEndpointResult{T}"/> so the
/// builder renders a shared missing-binding / forbidden / unavailable surface instead of fabricating data.
/// </summary>
public interface IStudioAnalysisContentClient
{
    Uri BaseUri { get; }

    Task<AnalysisEndpointResult<AnalysisContentSnapshot>> CreateItemAsync(
        CreateAnalysisContentItemRequest request,
        CancellationToken cancellationToken = default);

    Task<AnalysisEndpointResult<AnalysisContentSnapshot>> GetItemAsync(
        string itemId,
        CancellationToken cancellationToken = default);

    Task<AnalysisEndpointResult<AnalysisContentSnapshot>> GetLatestVersionAsync(
        string itemId,
        CancellationToken cancellationToken = default);

    Task<AnalysisEndpointResult<AnalysisContentSnapshot>> GetVersionAsync(
        string itemId,
        int contentVersion,
        CancellationToken cancellationToken = default);

    Task<AnalysisEndpointResult<AnalysisContentSnapshot>> CreateVersionAsync(
        string itemId,
        CreateAnalysisContentVersionRequest request,
        CancellationToken cancellationToken = default);

    Task<AnalysisEndpointResult<SavedQueryPreviewResult>> PreviewSavedQueryAsync(
        string itemId,
        int contentVersion,
        PreviewSavedQueryRequest request,
        CancellationToken cancellationToken = default);

    Task<AnalysisEndpointResult<AnalysisContentJobResponse>> SubmitRunAsync(
        string itemId,
        int contentVersion,
        RunAnalysisContentVersionRequest request,
        CancellationToken cancellationToken = default);

    Task<AnalysisEndpointResult<AnalysisContentJobResponse>> SubmitRerunAsync(
        string itemId,
        int contentVersion,
        RerunAnalysisContentVersionRequest request,
        CancellationToken cancellationToken = default);

    Task<AnalysisEndpointResult<AnalysisArtifactResolution>> GetArtifactAsync(
        string artifactId,
        CancellationToken cancellationToken = default);

    Task<AnalysisEndpointResult<AnalysisJobLogs>> GetJobLogsAsync(
        string jobId,
        int? limit = null,
        CancellationToken cancellationToken = default);

    Task<AnalysisEndpointResult<AnalysisJobFailure>> GetJobFailureAsync(
        string jobId,
        CancellationToken cancellationToken = default);
}

public sealed class HttpStudioAnalysisContentClient : IStudioAnalysisContentClient, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly string? _apiKey;

    public HttpStudioAnalysisContentClient(HttpClient httpClient, StudioAnalysisContentClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);

        BaseUri = options.BaseUri;
        _apiKey = options.ApiKey;
        _httpClient = httpClient;
        _httpClient.BaseAddress ??= BaseUri;
    }

    public Uri BaseUri { get; }

    public Task<AnalysisEndpointResult<AnalysisContentSnapshot>> CreateItemAsync(
        CreateAnalysisContentItemRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SendAsync<CreateAnalysisContentItemRequest, AnalysisContentSnapshot>(
            HttpMethod.Post,
            "/api/v1/analysis/content/items",
            request,
            "POST /api/v1/analysis/content/items",
            cancellationToken);
    }

    public Task<AnalysisEndpointResult<AnalysisContentSnapshot>> GetItemAsync(
        string itemId,
        CancellationToken cancellationToken = default) =>
        SendAsync<object, AnalysisContentSnapshot>(
            HttpMethod.Get,
            $"/api/v1/analysis/content/items/{Escape(itemId)}",
            null,
            "GET /api/v1/analysis/content/items/{itemId}",
            cancellationToken);

    public Task<AnalysisEndpointResult<AnalysisContentSnapshot>> GetLatestVersionAsync(
        string itemId,
        CancellationToken cancellationToken = default) =>
        SendAsync<object, AnalysisContentSnapshot>(
            HttpMethod.Get,
            $"/api/v1/analysis/content/items/{Escape(itemId)}/versions/latest",
            null,
            "GET /api/v1/analysis/content/items/{itemId}/versions/latest",
            cancellationToken);

    public Task<AnalysisEndpointResult<AnalysisContentSnapshot>> GetVersionAsync(
        string itemId,
        int contentVersion,
        CancellationToken cancellationToken = default) =>
        SendAsync<object, AnalysisContentSnapshot>(
            HttpMethod.Get,
            $"/api/v1/analysis/content/items/{Escape(itemId)}/versions/{contentVersion.ToString(CultureInfo.InvariantCulture)}",
            null,
            "GET /api/v1/analysis/content/items/{itemId}/versions/{contentVersion}",
            cancellationToken);

    public Task<AnalysisEndpointResult<AnalysisContentSnapshot>> CreateVersionAsync(
        string itemId,
        CreateAnalysisContentVersionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SendAsync<CreateAnalysisContentVersionRequest, AnalysisContentSnapshot>(
            HttpMethod.Post,
            $"/api/v1/analysis/content/items/{Escape(itemId)}/versions",
            request,
            "POST /api/v1/analysis/content/items/{itemId}/versions",
            cancellationToken);
    }

    public Task<AnalysisEndpointResult<SavedQueryPreviewResult>> PreviewSavedQueryAsync(
        string itemId,
        int contentVersion,
        PreviewSavedQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SendAsync<PreviewSavedQueryRequest, SavedQueryPreviewResult>(
            HttpMethod.Post,
            $"/api/v1/analysis/content/items/{Escape(itemId)}/versions/{contentVersion.ToString(CultureInfo.InvariantCulture)}/preview",
            request,
            "POST /api/v1/analysis/content/items/{itemId}/versions/{contentVersion}/preview",
            cancellationToken);
    }

    public Task<AnalysisEndpointResult<AnalysisContentJobResponse>> SubmitRunAsync(
        string itemId,
        int contentVersion,
        RunAnalysisContentVersionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SendAsync<RunAnalysisContentVersionRequest, AnalysisContentJobResponse>(
            HttpMethod.Post,
            $"/api/v1/analysis/content/items/{Escape(itemId)}/versions/{contentVersion.ToString(CultureInfo.InvariantCulture)}/runs",
            request,
            "POST /api/v1/analysis/content/items/{itemId}/versions/{contentVersion}/runs",
            cancellationToken);
    }

    public Task<AnalysisEndpointResult<AnalysisContentJobResponse>> SubmitRerunAsync(
        string itemId,
        int contentVersion,
        RerunAnalysisContentVersionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SendAsync<RerunAnalysisContentVersionRequest, AnalysisContentJobResponse>(
            HttpMethod.Post,
            $"/api/v1/analysis/content/items/{Escape(itemId)}/versions/{contentVersion.ToString(CultureInfo.InvariantCulture)}/reruns",
            request,
            "POST /api/v1/analysis/content/items/{itemId}/versions/{contentVersion}/reruns",
            cancellationToken);
    }

    public Task<AnalysisEndpointResult<AnalysisArtifactResolution>> GetArtifactAsync(
        string artifactId,
        CancellationToken cancellationToken = default) =>
        SendAsync<object, AnalysisArtifactResolution>(
            HttpMethod.Get,
            $"/api/v1/analysis/artifacts/{Escape(artifactId)}",
            null,
            "GET /api/v1/analysis/artifacts/{artifactId}",
            cancellationToken);

    public Task<AnalysisEndpointResult<AnalysisJobLogs>> GetJobLogsAsync(
        string jobId,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        var path = $"/api/v1/analysis/jobs/{Escape(jobId)}/logs";
        if (limit is { } value)
        {
            path += $"?limit={value.ToString(CultureInfo.InvariantCulture)}";
        }

        return SendAsync<object, AnalysisJobLogs>(
            HttpMethod.Get,
            path,
            null,
            "GET /api/v1/analysis/jobs/{jobId}/logs",
            cancellationToken);
    }

    public Task<AnalysisEndpointResult<AnalysisJobFailure>> GetJobFailureAsync(
        string jobId,
        CancellationToken cancellationToken = default) =>
        SendAsync<object, AnalysisJobFailure>(
            HttpMethod.Get,
            $"/api/v1/analysis/jobs/{Escape(jobId)}/failure",
            null,
            "GET /api/v1/analysis/jobs/{jobId}/failure",
            cancellationToken);

    public void Dispose() => _httpClient.Dispose();

    private static string Escape(string segment) => Uri.EscapeDataString(segment ?? string.Empty);

    private async Task<AnalysisEndpointResult<TResponse>> SendAsync<TBody, TResponse>(
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
            return AnalysisEndpointResult<TResponse>.FromIssue(new AnalysisEndpointIssue(
                "Unavailable",
                contract,
                $"The Honua server analysis endpoint could not be reached: {ex.Message}"));
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            return AnalysisEndpointResult<TResponse>.FromIssue(new AnalysisEndpointIssue(
                "Unavailable",
                contract,
                $"The Honua server analysis endpoint could not be reached: {ex.Message}"));
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                return AnalysisEndpointResult<TResponse>.FromIssue(CreateIssue(contract, response.StatusCode));
            }

            TResponse? payload;
            try
            {
                // Analysis Content responses are plain camelCase JSON DTOs (no ApiResponse<T> envelope).
                payload = await response.Content
                    .ReadFromJsonAsync<TResponse>(JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (JsonException ex)
            {
                return AnalysisEndpointResult<TResponse>.FromIssue(new AnalysisEndpointIssue(
                    "Unsupported",
                    contract,
                    $"The Honua server analysis response did not match the expected API shape: {ex.Message}",
                    (int)response.StatusCode));
            }

            if (payload is null)
            {
                return AnalysisEndpointResult<TResponse>.FromIssue(new AnalysisEndpointIssue(
                    "Unavailable",
                    contract,
                    "The Honua server analysis response body was empty.",
                    (int)response.StatusCode));
            }

            return AnalysisEndpointResult<TResponse>.FromData(payload);
        }
    }

    private static AnalysisEndpointIssue CreateIssue(string contract, HttpStatusCode statusCode)
    {
        var state = statusCode switch
        {
            HttpStatusCode.BadRequest => "Invalid request",
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "Missing permission",
            HttpStatusCode.NotFound => "Not found",
            HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotImplemented => "Unsupported",
            HttpStatusCode.Conflict => "Conflict",
            _ => "Unavailable"
        };

        var detail = statusCode switch
        {
            HttpStatusCode.BadRequest => "The Honua server rejected the analysis request as invalid (bad payload, "
                + "mismatched content kind, invalid limit, bad filter plan, or unknown override key).",
            HttpStatusCode.Unauthorized => "The Honua server rejected the analysis request because admin authentication is missing.",
            HttpStatusCode.Forbidden => "The Honua server rejected the analysis request because the current principal lacks admin permission.",
            HttpStatusCode.NotFound => "The Honua server could not find the requested analysis item, version, job, or artifact.",
            HttpStatusCode.MethodNotAllowed => "The Honua server exposes the route but not the required analysis API verb.",
            HttpStatusCode.NotImplemented => "The Honua server reports this analysis capability is not implemented.",
            HttpStatusCode.Conflict => "The analysis request conflicted with server state (version conflict, the job has not "
                + "failed, or a geoprocessing precondition failed); reload before retrying.",
            HttpStatusCode.ServiceUnavailable => "The backing analysis content, job, or log store is unavailable.",
            _ => string.Format(
                CultureInfo.InvariantCulture,
                "The Honua server returned HTTP {0} ({1}) for the analysis request.",
                (int)statusCode,
                statusCode)
        };

        return new AnalysisEndpointIssue(state, contract, detail, (int)statusCode);
    }
}

#endregion
