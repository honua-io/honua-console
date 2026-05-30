using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Honua.Console.Contracts;

// SHIM(honua-sdk-dotnet#169): the honua-server Studio package lifecycle + validation/preview API
// (#1180 commit 52202d7fc, #1181 commit 6d661afb8) is merged to honua-server trunk, but
// honua-sdk-dotnet does not yet project these Studio DTOs (no consumable Honua.Sdk Studio package,
// and honua-console wires no SDK NuGet feed). Per SDK_SHIM_POLICY.md the wire records and the thin
// HTTP client live behind this single Console contracts boundary until honua-sdk-dotnet#169 publishes
// the Studio projection and honua-console#7 swaps to SDK types. Do not add a sibling-repo
// ProjectReference; do not mirror these DTOs anywhere else. The wire shape mirrors
// honua-server/src/Honua.Core/Features/Studio/Domain/StudioPackageModels.cs + StudioPackageEnums.cs
// (camelCase, string enums via [JsonStringEnumMemberName], null props omitted) and the shared
// ApiResponse<T> envelope. See SDK_SHIM_POLICY "Active Shims".

#region Enums (StudioPackageEnums.cs)

[JsonConverter(typeof(JsonStringEnumConverter<StudioPackageFamily>))]
public enum StudioPackageFamily
{
    [JsonStringEnumMemberName("query")] Query,
    [JsonStringEnumMemberName("analysis")] Analysis,
    [JsonStringEnumMemberName("map")] Map,
    [JsonStringEnumMemberName("dashboard")] Dashboard,
    [JsonStringEnumMemberName("report")] Report,
    [JsonStringEnumMemberName("form")] Form,
    [JsonStringEnumMemberName("app")] App,
    [JsonStringEnumMemberName("workflow")] Workflow,
    [JsonStringEnumMemberName("gp")] Geoprocessing,
    [JsonStringEnumMemberName("etl")] Etl
}

[JsonConverter(typeof(JsonStringEnumConverter<StudioPackageOperation>))]
public enum StudioPackageOperation
{
    [JsonStringEnumMemberName("draft.create")] DraftCreate,
    [JsonStringEnumMemberName("draft.read")] DraftRead,
    [JsonStringEnumMemberName("draft.update")] DraftUpdate,
    [JsonStringEnumMemberName("validate")] Validate,
    [JsonStringEnumMemberName("preview-plan")] PreviewPlan,
    [JsonStringEnumMemberName("content-version.create")] ContentVersionCreate,
    [JsonStringEnumMemberName("content-version.read")] ContentVersionRead,
    [JsonStringEnumMemberName("content-version.compare")] ContentVersionCompare,
    [JsonStringEnumMemberName("publish-request.create")] PublishRequestCreate,
    [JsonStringEnumMemberName("reopen")] Reopen,
    [JsonStringEnumMemberName("rollback")] Rollback
}

[JsonConverter(typeof(JsonStringEnumConverter<StudioPackageSupportLevel>))]
public enum StudioPackageSupportLevel
{
    [JsonStringEnumMemberName("unsupported")] Unsupported,
    [JsonStringEnumMemberName("limited")] Limited,
    [JsonStringEnumMemberName("supported")] Supported
}

[JsonConverter(typeof(JsonStringEnumConverter<StudioPackagePersistenceMode>))]
public enum StudioPackagePersistenceMode
{
    [JsonStringEnumMemberName("in-memory")] InMemory,
    [JsonStringEnumMemberName("durable")] Durable
}

[JsonConverter(typeof(JsonStringEnumConverter<StudioPackageValidationStatus>))]
public enum StudioPackageValidationStatus
{
    [JsonStringEnumMemberName("not-validated")] NotValidated,
    [JsonStringEnumMemberName("valid")] Valid,
    [JsonStringEnumMemberName("warning")] Warning,
    [JsonStringEnumMemberName("invalid")] Invalid
}

[JsonConverter(typeof(JsonStringEnumConverter<StudioPackageDiagnosticSeverity>))]
public enum StudioPackageDiagnosticSeverity
{
    [JsonStringEnumMemberName("info")] Info,
    [JsonStringEnumMemberName("warning")] Warning,
    [JsonStringEnumMemberName("error")] Error,
    [JsonStringEnumMemberName("blocker")] Blocker
}

[JsonConverter(typeof(JsonStringEnumConverter<StudioPublicationRequestStatus>))]
public enum StudioPublicationRequestStatus
{
    [JsonStringEnumMemberName("accepted")] Accepted,
    [JsonStringEnumMemberName("pending")] Pending,
    [JsonStringEnumMemberName("rejected")] Rejected
}

[JsonConverter(typeof(JsonStringEnumConverter<StudioRollbackPointer>))]
public enum StudioRollbackPointer
{
    [JsonStringEnumMemberName("current")] Current,
    [JsonStringEnumMemberName("published")] Published,
    [JsonStringEnumMemberName("both")] Both
}

#endregion

#region Domain DTOs (StudioPackageModels.cs)

public sealed record StudioPackageBinding
{
    [JsonPropertyName("key")] public string Key { get; init; } = string.Empty;
    [JsonPropertyName("kind")] public string Kind { get; init; } = string.Empty;
    [JsonPropertyName("ref")] public string Ref { get; init; } = string.Empty;
    [JsonPropertyName("crs")] public string? Crs { get; init; }
    [JsonPropertyName("srid")] public int? Srid { get; init; }
    [JsonPropertyName("units")] public string? Units { get; init; }
    [JsonPropertyName("requiredPermissions")] public IReadOnlyList<string> RequiredPermissions { get; init; } = [];
    [JsonPropertyName("metadata")] public JsonElement? Metadata { get; init; }
}

public sealed record StudioPackageDependency
{
    [JsonPropertyName("ref")] public string Ref { get; init; } = string.Empty;
    [JsonPropertyName("kind")] public string Kind { get; init; } = string.Empty;
    [JsonPropertyName("versionId")] public string? VersionId { get; init; }
    [JsonPropertyName("required")] public bool Required { get; init; } = true;
    [JsonPropertyName("metadata")] public JsonElement? Metadata { get; init; }
}

public sealed record StudioProvenanceRef
{
    [JsonPropertyName("kind")] public string Kind { get; init; } = string.Empty;
    [JsonPropertyName("ref")] public string Ref { get; init; } = string.Empty;
    [JsonPropertyName("rel")] public string Rel { get; init; } = string.Empty;
    [JsonPropertyName("actorId")] public string? ActorId { get; init; }
    [JsonPropertyName("timestamp")] public DateTimeOffset? Timestamp { get; init; }
}

public sealed record StudioPublicationIntent
{
    [JsonPropertyName("route")] public string? Route { get; init; }
    [JsonPropertyName("visibility")] public string? Visibility { get; init; }
    [JsonPropertyName("embed")] public bool? Embed { get; init; }
    [JsonPropertyName("service")] public string? Service { get; init; }
    [JsonPropertyName("schedule")] public string? Schedule { get; init; }
    [JsonPropertyName("job")] public string? Job { get; init; }
    [JsonPropertyName("metadata")] public JsonElement? Metadata { get; init; }
}

public sealed record StudioValidationDiagnostic
{
    [JsonPropertyName("code")] public string Code { get; init; } = string.Empty;
    [JsonPropertyName("severity")] public StudioPackageDiagnosticSeverity Severity { get; init; }
    [JsonPropertyName("path")] public string? Path { get; init; }
    [JsonPropertyName("message")] public string Message { get; init; } = string.Empty;
}

public sealed record StudioValidationSummary
{
    [JsonPropertyName("status")] public StudioPackageValidationStatus Status { get; init; }
    [JsonPropertyName("diagnostics")] public IReadOnlyList<StudioValidationDiagnostic> Diagnostics { get; init; } = [];
    [JsonPropertyName("unsupportedCapabilities")] public IReadOnlyList<string> UnsupportedCapabilities { get; init; } = [];
    [JsonPropertyName("generatedAt")] public DateTimeOffset? GeneratedAt { get; init; }
}

public sealed record StudioPackageEnvelope
{
    [JsonPropertyName("family")] public StudioPackageFamily Family { get; init; }
    [JsonPropertyName("schemaVersion")] public string SchemaVersion { get; init; } = string.Empty;
    [JsonPropertyName("format")] public string? Format { get; init; }
    [JsonPropertyName("bindings")] public IReadOnlyList<StudioPackageBinding> Bindings { get; init; } = [];
    [JsonPropertyName("dependencies")] public IReadOnlyList<StudioPackageDependency> Dependencies { get; init; } = [];
    // Server defaults Validation to NotValidated and computes it; nullable here so create/update bodies
    // can omit the server-owned summary (DefaultIgnoreCondition = WhenWritingNull).
    [JsonPropertyName("validation")] public StudioValidationSummary? Validation { get; init; }
    [JsonPropertyName("publicationIntent")] public StudioPublicationIntent? PublicationIntent { get; init; }
    [JsonPropertyName("provenance")] public IReadOnlyList<StudioProvenanceRef> Provenance { get; init; } = [];
    [JsonPropertyName("body")] public JsonElement? Body { get; init; }
}

public sealed record StudioPackageFamilyDescriptor
{
    [JsonPropertyName("family")] public StudioPackageFamily Family { get; init; }
    [JsonPropertyName("currentSchemaVersion")] public string CurrentSchemaVersion { get; init; } = string.Empty;
    [JsonPropertyName("format")] public string Format { get; init; } = string.Empty;
    [JsonPropertyName("supportLevel")] public StudioPackageSupportLevel SupportLevel { get; init; }
    [JsonPropertyName("supportedOperations")] public IReadOnlyList<StudioPackageOperation> SupportedOperations { get; init; } = [];
    [JsonPropertyName("validationDepth")] public string ValidationDepth { get; init; } = string.Empty;
    [JsonPropertyName("limitations")] public IReadOnlyList<string> Limitations { get; init; } = [];
    [JsonPropertyName("maxPackageBytes")] public int MaxPackageBytes { get; init; }
    [JsonPropertyName("previewSupported")] public bool PreviewSupported { get; init; }
    [JsonPropertyName("publishSupported")] public bool PublishSupported { get; init; }
}

public sealed record StudioPackageFamilyCapabilities
{
    [JsonPropertyName("persistenceMode")] public StudioPackagePersistenceMode PersistenceMode { get; init; }
    [JsonPropertyName("durable")] public bool Durable { get; init; }
    [JsonPropertyName("families")] public IReadOnlyList<StudioPackageFamilyDescriptor> Families { get; init; } = [];
}

public sealed record StudioPackageDraft
{
    [JsonPropertyName("draftId")] public Guid DraftId { get; init; }
    [JsonPropertyName("itemId")] public Guid ItemId { get; init; }
    [JsonPropertyName("packageKey")] public string PackageKey { get; init; } = string.Empty;
    [JsonPropertyName("workspaceId")] public string? WorkspaceId { get; init; }
    [JsonPropertyName("ownerId")] public string? OwnerId { get; init; }
    [JsonPropertyName("family")] public StudioPackageFamily Family { get; init; }
    [JsonPropertyName("envelope")] public StudioPackageEnvelope Envelope { get; init; } = new();
    [JsonPropertyName("validation")] public StudioValidationSummary Validation { get; init; } = new();
    [JsonPropertyName("baseVersionId")] public Guid? BaseVersionId { get; init; }
    [JsonPropertyName("generation")] public long Generation { get; init; }
    [JsonPropertyName("createdBy")] public string? CreatedBy { get; init; }
    [JsonPropertyName("updatedBy")] public string? UpdatedBy { get; init; }
    [JsonPropertyName("createdAt")] public DateTimeOffset CreatedAt { get; init; }
    [JsonPropertyName("updatedAt")] public DateTimeOffset UpdatedAt { get; init; }
}

public sealed record StudioPreviewPlan
{
    [JsonPropertyName("draftId")] public Guid DraftId { get; init; }
    [JsonPropertyName("family")] public StudioPackageFamily Family { get; init; }
    [JsonPropertyName("synchronous")] public bool Synchronous { get; init; }
    [JsonPropertyName("requiresJob")] public bool RequiresJob { get; init; }
    [JsonPropertyName("steps")] public IReadOnlyList<string> Steps { get; init; } = [];
    [JsonPropertyName("validation")] public StudioValidationSummary Validation { get; init; } = new();
}

public sealed record StudioContentVersion
{
    [JsonPropertyName("itemId")] public Guid ItemId { get; init; }
    [JsonPropertyName("packageKey")] public string PackageKey { get; init; } = string.Empty;
    [JsonPropertyName("workspaceId")] public string? WorkspaceId { get; init; }
    [JsonPropertyName("ownerId")] public string? OwnerId { get; init; }
    [JsonPropertyName("versionId")] public Guid VersionId { get; init; }
    [JsonPropertyName("versionNumber")] public int VersionNumber { get; init; }
    [JsonPropertyName("contentHash")] public string ContentHash { get; init; } = string.Empty;
    [JsonPropertyName("envelope")] public StudioPackageEnvelope Envelope { get; init; } = new();
    [JsonPropertyName("validation")] public StudioValidationSummary Validation { get; init; } = new();
    [JsonPropertyName("dependencies")] public IReadOnlyList<StudioPackageDependency> Dependencies { get; init; } = [];
    [JsonPropertyName("provenance")] public IReadOnlyList<StudioProvenanceRef> Provenance { get; init; } = [];
    [JsonPropertyName("sourceDraftId")] public Guid? SourceDraftId { get; init; }
    [JsonPropertyName("baseVersionId")] public Guid? BaseVersionId { get; init; }
    [JsonPropertyName("changeNote")] public string? ChangeNote { get; init; }
    [JsonPropertyName("createdBy")] public string? CreatedBy { get; init; }
    [JsonPropertyName("createdAt")] public DateTimeOffset CreatedAt { get; init; }
}

public sealed record StudioContentVersionListResponse
{
    [JsonPropertyName("itemId")] public Guid ItemId { get; init; }
    [JsonPropertyName("versions")] public IReadOnlyList<StudioContentVersion> Versions { get; init; } = [];
}

public sealed record StudioPublicationRequest
{
    [JsonPropertyName("requestId")] public Guid RequestId { get; init; }
    [JsonPropertyName("itemId")] public Guid ItemId { get; init; }
    [JsonPropertyName("versionId")] public Guid VersionId { get; init; }
    [JsonPropertyName("intent")] public StudioPublicationIntent? Intent { get; init; }
    [JsonPropertyName("status")] public StudioPublicationRequestStatus Status { get; init; }
    [JsonPropertyName("validation")] public StudioValidationSummary Validation { get; init; } = new();
    [JsonPropertyName("warningAcknowledgement")] public string? WarningAcknowledgement { get; init; }
    [JsonPropertyName("requestedBy")] public string? RequestedBy { get; init; }
    [JsonPropertyName("createdAt")] public DateTimeOffset CreatedAt { get; init; }
}

public sealed record StudioContentItemPointers
{
    [JsonPropertyName("itemId")] public Guid ItemId { get; init; }
    [JsonPropertyName("currentVersionId")] public Guid? CurrentVersionId { get; init; }
    [JsonPropertyName("publishedVersionId")] public Guid? PublishedVersionId { get; init; }
}

public sealed record StudioRollbackRequest
{
    [JsonPropertyName("requestId")] public Guid RequestId { get; init; }
    [JsonPropertyName("itemId")] public Guid ItemId { get; init; }
    [JsonPropertyName("targetVersionId")] public Guid TargetVersionId { get; init; }
    [JsonPropertyName("pointer")] public StudioRollbackPointer Target { get; init; }
    [JsonPropertyName("pointers")] public StudioContentItemPointers Pointers { get; init; } = new();
    [JsonPropertyName("requestedBy")] public string? RequestedBy { get; init; }
    [JsonPropertyName("reason")] public string? Reason { get; init; }
    [JsonPropertyName("createdAt")] public DateTimeOffset CreatedAt { get; init; }
}

#endregion

#region Request bodies (StudioApiModels.cs)

public sealed record CreateStudioPackageDraftRequest
{
    [JsonPropertyName("itemId")] public Guid? ItemId { get; init; }
    [JsonPropertyName("packageKey")] public string PackageKey { get; init; } = string.Empty;
    [JsonPropertyName("workspaceId")] public string? WorkspaceId { get; init; }
    [JsonPropertyName("ownerId")] public string? OwnerId { get; init; }
    [JsonPropertyName("envelope")] public StudioPackageEnvelope Envelope { get; init; } = new();
}

public sealed record UpdateStudioPackageDraftRequest
{
    [JsonPropertyName("packageKey")] public string PackageKey { get; init; } = string.Empty;
    [JsonPropertyName("workspaceId")] public string? WorkspaceId { get; init; }
    [JsonPropertyName("ownerId")] public string? OwnerId { get; init; }
    [JsonPropertyName("envelope")] public StudioPackageEnvelope Envelope { get; init; } = new();
    [JsonPropertyName("generation")] public long? Generation { get; init; }
}

public sealed record SaveStudioContentVersionRequest
{
    [JsonPropertyName("changeNote")] public string? ChangeNote { get; init; }
}

public sealed record CreateStudioPublicationRequest
{
    [JsonPropertyName("intent")] public StudioPublicationIntent? Intent { get; init; }
    [JsonPropertyName("warningAcknowledgement")] public string? WarningAcknowledgement { get; init; }
}

public sealed record CreateStudioRollbackRequest
{
    [JsonPropertyName("targetVersionId")] public Guid TargetVersionId { get; init; }
    [JsonPropertyName("pointer")] public StudioRollbackPointer Target { get; init; } = StudioRollbackPointer.Current;
    [JsonPropertyName("reason")] public string? Reason { get; init; }
}

#endregion

#region Envelope + result types

public sealed record StudioApiResponse<T>(
    bool Success,
    T? Data,
    string? Message,
    DateTimeOffset? Timestamp);

public sealed record StudioEndpointResult<T>(T? Data, StudioEndpointIssue? Issue)
{
    public bool IsSuccess => Issue is null;

    public static StudioEndpointResult<T> FromData(T data) => new(data, null);

    public static StudioEndpointResult<T> FromIssue(StudioEndpointIssue issue) => new(default, issue);
}

public sealed record StudioEndpointIssue(
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

public sealed record StudioPackageLifecycleClientOptions(Uri BaseUri, string? ApiKey = null);

public interface IStudioPackageLifecycleClient
{
    Uri BaseUri { get; }

    Task<StudioEndpointResult<StudioPackageFamilyCapabilities>> ListPackageFamiliesAsync(
        CancellationToken cancellationToken = default);

    Task<StudioEndpointResult<StudioPackageDraft>> CreatePackageDraftAsync(
        CreateStudioPackageDraftRequest request,
        CancellationToken cancellationToken = default);

    Task<StudioEndpointResult<StudioPackageDraft>> GetPackageDraftAsync(
        Guid draftId,
        CancellationToken cancellationToken = default);

    Task<StudioEndpointResult<StudioPackageDraft>> UpdatePackageDraftAsync(
        Guid draftId,
        UpdateStudioPackageDraftRequest request,
        CancellationToken cancellationToken = default);

    Task<StudioEndpointResult<StudioValidationSummary>> ValidatePackageDraftAsync(
        Guid draftId,
        CancellationToken cancellationToken = default);

    Task<StudioEndpointResult<StudioPreviewPlan>> CreatePreviewPlanAsync(
        Guid draftId,
        CancellationToken cancellationToken = default);

    Task<StudioEndpointResult<StudioContentVersion>> SaveContentVersionAsync(
        Guid draftId,
        SaveStudioContentVersionRequest request,
        CancellationToken cancellationToken = default);

    Task<StudioEndpointResult<StudioContentVersionListResponse>> ListContentVersionsAsync(
        Guid itemId,
        CancellationToken cancellationToken = default);

    Task<StudioEndpointResult<StudioContentVersion>> GetContentVersionAsync(
        Guid itemId,
        Guid versionId,
        CancellationToken cancellationToken = default);

    Task<StudioEndpointResult<StudioPublicationRequest>> CreatePublishRequestAsync(
        Guid itemId,
        Guid versionId,
        CreateStudioPublicationRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reopens an immutable content version as a fresh editable draft (server route
    /// <c>POST /api/v1/studio/content-items/{itemId}/versions/{versionId}/reopen</c>). The server clones the
    /// version body into a new draft generation, so the published version is never mutated in place;
    /// subsequent saves create new content versions instead.
    /// </summary>
    Task<StudioEndpointResult<StudioPackageDraft>> ReopenContentVersionAsync(
        Guid itemId,
        Guid versionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reopens an immutable content version as a fresh editable draft (server route
    /// <c>POST /api/v1/studio/content-items/{itemId}/versions/{versionId}/reopen</c>). Alias retained for
    /// the dashboard/app builder lifecycle bindings.
    /// </summary>
    Task<StudioEndpointResult<StudioPackageDraft>> ReopenVersionAsync(
        Guid itemId,
        Guid versionId,
        CancellationToken cancellationToken = default);

    /// <summary>Requests a rollback to a prior published version, repointing the content item's
    /// current/published pointer to an earlier immutable version (server route
    /// <c>POST /api/v1/studio/content-items/{itemId}/rollback-requests</c>).</summary>
    Task<StudioEndpointResult<StudioRollbackRequest>> RollbackAsync(
        Guid itemId,
        CreateStudioRollbackRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Alias for <see cref="RollbackAsync"/> retained for the app builder lifecycle binding.</summary>
    Task<StudioEndpointResult<StudioRollbackRequest>> CreateRollbackRequestAsync(
        Guid itemId,
        CreateStudioRollbackRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class HttpStudioPackageLifecycleClient : IStudioPackageLifecycleClient, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly string? _apiKey;

    public HttpStudioPackageLifecycleClient(HttpClient httpClient, StudioPackageLifecycleClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);

        BaseUri = options.BaseUri;
        _apiKey = options.ApiKey;
        _httpClient = httpClient;
        _httpClient.BaseAddress ??= BaseUri;
    }

    public Uri BaseUri { get; }

    public Task<StudioEndpointResult<StudioPackageFamilyCapabilities>> ListPackageFamiliesAsync(
        CancellationToken cancellationToken = default) =>
        SendAsync<object, StudioPackageFamilyCapabilities>(
            HttpMethod.Get,
            "/api/v1/studio/package-families",
            null,
            "GET /api/v1/studio/package-families",
            cancellationToken);

    public Task<StudioEndpointResult<StudioPackageDraft>> CreatePackageDraftAsync(
        CreateStudioPackageDraftRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SendAsync<CreateStudioPackageDraftRequest, StudioPackageDraft>(
            HttpMethod.Post,
            "/api/v1/studio/package-drafts",
            request,
            "POST /api/v1/studio/package-drafts",
            cancellationToken);
    }

    public Task<StudioEndpointResult<StudioPackageDraft>> GetPackageDraftAsync(
        Guid draftId,
        CancellationToken cancellationToken = default) =>
        SendAsync<object, StudioPackageDraft>(
            HttpMethod.Get,
            $"/api/v1/studio/package-drafts/{draftId}",
            null,
            "GET /api/v1/studio/package-drafts/{draftId}",
            cancellationToken);

    public Task<StudioEndpointResult<StudioPackageDraft>> UpdatePackageDraftAsync(
        Guid draftId,
        UpdateStudioPackageDraftRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SendAsync<UpdateStudioPackageDraftRequest, StudioPackageDraft>(
            HttpMethod.Put,
            $"/api/v1/studio/package-drafts/{draftId}",
            request,
            "PUT /api/v1/studio/package-drafts/{draftId}",
            cancellationToken);
    }

    public Task<StudioEndpointResult<StudioValidationSummary>> ValidatePackageDraftAsync(
        Guid draftId,
        CancellationToken cancellationToken = default) =>
        SendAsync<object, StudioValidationSummary>(
            HttpMethod.Post,
            $"/api/v1/studio/package-drafts/{draftId}/validate",
            null,
            "POST /api/v1/studio/package-drafts/{draftId}/validate",
            cancellationToken);

    public Task<StudioEndpointResult<StudioPreviewPlan>> CreatePreviewPlanAsync(
        Guid draftId,
        CancellationToken cancellationToken = default) =>
        SendAsync<object, StudioPreviewPlan>(
            HttpMethod.Post,
            $"/api/v1/studio/package-drafts/{draftId}/preview-plan",
            null,
            "POST /api/v1/studio/package-drafts/{draftId}/preview-plan",
            cancellationToken);

    public Task<StudioEndpointResult<StudioContentVersion>> SaveContentVersionAsync(
        Guid draftId,
        SaveStudioContentVersionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SendAsync<SaveStudioContentVersionRequest, StudioContentVersion>(
            HttpMethod.Post,
            $"/api/v1/studio/package-drafts/{draftId}/content-versions",
            request,
            "POST /api/v1/studio/package-drafts/{draftId}/content-versions",
            cancellationToken);
    }

    public Task<StudioEndpointResult<StudioContentVersionListResponse>> ListContentVersionsAsync(
        Guid itemId,
        CancellationToken cancellationToken = default) =>
        SendAsync<object, StudioContentVersionListResponse>(
            HttpMethod.Get,
            $"/api/v1/studio/content-items/{itemId}/versions",
            null,
            "GET /api/v1/studio/content-items/{itemId}/versions",
            cancellationToken);

    public Task<StudioEndpointResult<StudioContentVersion>> GetContentVersionAsync(
        Guid itemId,
        Guid versionId,
        CancellationToken cancellationToken = default) =>
        SendAsync<object, StudioContentVersion>(
            HttpMethod.Get,
            $"/api/v1/studio/content-items/{itemId}/versions/{versionId}",
            null,
            "GET /api/v1/studio/content-items/{itemId}/versions/{versionId}",
            cancellationToken);

    public Task<StudioEndpointResult<StudioPublicationRequest>> CreatePublishRequestAsync(
        Guid itemId,
        Guid versionId,
        CreateStudioPublicationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SendAsync<CreateStudioPublicationRequest, StudioPublicationRequest>(
            HttpMethod.Post,
            $"/api/v1/studio/content-items/{itemId}/versions/{versionId}/publish-requests",
            request,
            "POST /api/v1/studio/content-items/{itemId}/versions/{versionId}/publish-requests",
            cancellationToken);
    }

    public Task<StudioEndpointResult<StudioPackageDraft>> ReopenContentVersionAsync(
        Guid itemId,
        Guid versionId,
        CancellationToken cancellationToken = default) =>
        SendAsync<object, StudioPackageDraft>(
            HttpMethod.Post,
            $"/api/v1/studio/content-items/{itemId}/versions/{versionId}/reopen",
            null,
            "POST /api/v1/studio/content-items/{itemId}/versions/{versionId}/reopen",
            cancellationToken);

    public Task<StudioEndpointResult<StudioPackageDraft>> ReopenVersionAsync(
        Guid itemId,
        Guid versionId,
        CancellationToken cancellationToken = default) =>
        ReopenContentVersionAsync(itemId, versionId, cancellationToken);

    public Task<StudioEndpointResult<StudioRollbackRequest>> RollbackAsync(
        Guid itemId,
        CreateStudioRollbackRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SendAsync<CreateStudioRollbackRequest, StudioRollbackRequest>(
            HttpMethod.Post,
            $"/api/v1/studio/content-items/{itemId}/rollback-requests",
            request,
            "POST /api/v1/studio/content-items/{itemId}/rollback-requests",
            cancellationToken);
    }

    public Task<StudioEndpointResult<StudioRollbackRequest>> CreateRollbackRequestAsync(
        Guid itemId,
        CreateStudioRollbackRequest request,
        CancellationToken cancellationToken = default) =>
        RollbackAsync(itemId, request, cancellationToken);

    public void Dispose() => _httpClient.Dispose();

    private async Task<StudioEndpointResult<TResponse>> SendAsync<TBody, TResponse>(
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
            return StudioEndpointResult<TResponse>.FromIssue(new StudioEndpointIssue(
                "Unavailable",
                contract,
                $"The Honua server Studio endpoint could not be reached: {ex.Message}"));
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            return StudioEndpointResult<TResponse>.FromIssue(new StudioEndpointIssue(
                "Unavailable",
                contract,
                $"The Honua server Studio endpoint could not be reached: {ex.Message}"));
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                return StudioEndpointResult<TResponse>.FromIssue(CreateIssue(contract, response.StatusCode));
            }

            StudioApiResponse<TResponse>? envelope;
            try
            {
                envelope = await response.Content
                    .ReadFromJsonAsync<StudioApiResponse<TResponse>>(JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (JsonException ex)
            {
                return StudioEndpointResult<TResponse>.FromIssue(new StudioEndpointIssue(
                    "Unsupported",
                    contract,
                    $"The Honua server Studio response did not match the expected API shape: {ex.Message}",
                    (int)response.StatusCode));
            }

            if (envelope?.Success == true && envelope.Data is not null)
            {
                return StudioEndpointResult<TResponse>.FromData(envelope.Data);
            }

            return StudioEndpointResult<TResponse>.FromIssue(new StudioEndpointIssue(
                "Unavailable",
                contract,
                envelope?.Message ?? "The Honua server Studio response did not include data.",
                (int)response.StatusCode));
        }
    }

    private static StudioEndpointIssue CreateIssue(string contract, HttpStatusCode statusCode)
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
            HttpStatusCode.Unauthorized => "The Honua server rejected the Studio request because admin authentication is missing.",
            HttpStatusCode.Forbidden => "The Honua server rejected the Studio request because the current principal lacks admin permission.",
            HttpStatusCode.NotFound => "The Honua server does not expose this Studio package contract, or the draft/version was not found.",
            HttpStatusCode.MethodNotAllowed => "The Honua server exposes the route but not the required Studio API verb.",
            HttpStatusCode.NotImplemented => "The Honua server reports this Studio capability is not implemented.",
            HttpStatusCode.Conflict => "The Studio draft changed on the server (optimistic concurrency); reload before retrying.",
            _ => string.Format(
                CultureInfo.InvariantCulture,
                "The Honua server returned HTTP {0} ({1}) for the Studio request.",
                (int)statusCode,
                statusCode)
        };

        return new StudioEndpointIssue(state, contract, detail, (int)statusCode);
    }
}

#endregion
