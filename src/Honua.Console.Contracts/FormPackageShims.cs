using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Honua.Console.Contracts;

// SHIM(honua-server#1184 / honua-sdk-dotnet#166): honua-server owns the form package
// content/publication contract (FormPackage* records in Honua.Core.Features.Forms.Packages,
// admin lifecycle under /api/v1/admin/forms/packages and runtime offline-policy under
// /api/v1/forms/packages). honua-sdk-dotnet does not yet project these as a consumable stable
// package, and honua-console wires no SDK NuGet feed (see SDK_SHIM_POLICY.md and the Operate
// admin shim above). Per the Console Patterns Charter section 11 and SDK_SHIM_POLICY, the
// form authoring surface binds through a thin HttpClient behind this single Honua.Console.Contracts
// boundary: the wire records below mirror the server document graph, and the client speaks the
// real admin lifecycle. Do not add a sibling-repo ProjectReference. Swap these for SDK types when
// honua-sdk-dotnet ships a consumable form-package projection and honua-console#7 wires the feed.
//
// The form authoring routes are admin routes, so this client reuses the shared
// HonuaAdminEndpointResult<T>/HonuaAdminEndpointIssue envelope (defined in OperateAdminShims.cs).
// Unlike the Operate admin endpoints, the forms endpoints return the DTO directly rather than a
// {success,data,message} wrapper, so this client deserializes the body straight into the contract
// type and maps form-specific status semantics (404 not found, 409 ETag/draft conflict, 428 missing
// If-Match) to issues.
public sealed record HonuaFormPackageClientOptions(Uri BaseUri, string? ApiKey = null);

public interface IHonuaFormPackageClient
{
    Uri BaseUri { get; }

    Task<HonuaAdminEndpointResult<HonuaFormPackageSummary[]>> ListPackagesAsync(
        CancellationToken cancellationToken = default);

    Task<HonuaAdminEndpointResult<HonuaFormPackageVersion>> CreateDraftAsync(
        HonuaFormPackageDocument document,
        CancellationToken cancellationToken = default);

    Task<HonuaAdminEndpointResult<HonuaFormPackageVersion>> GetCurrentAsync(
        string formId,
        CancellationToken cancellationToken = default);

    Task<HonuaAdminEndpointResult<HonuaFormPackageVersion[]>> ListVersionsAsync(
        string formId,
        CancellationToken cancellationToken = default);

    Task<HonuaAdminEndpointResult<HonuaFormPackageVersion>> GetVersionAsync(
        string formId,
        int packageVersion,
        CancellationToken cancellationToken = default);

    Task<HonuaAdminEndpointResult<HonuaFormPackageVersion>> UpdateDraftAsync(
        string formId,
        int packageVersion,
        string etag,
        HonuaFormPackageDocument document,
        CancellationToken cancellationToken = default);

    Task<HonuaAdminEndpointResult<HonuaFormPackageValidationResult>> ValidateAsync(
        string formId,
        int packageVersion,
        CancellationToken cancellationToken = default);

    Task<HonuaAdminEndpointResult<HonuaFormPackageVersion>> PublishAsync(
        string formId,
        int packageVersion,
        CancellationToken cancellationToken = default);

    Task<HonuaAdminEndpointResult<HonuaFormPackageVersion>> ReopenAsync(
        string formId,
        int packageVersion,
        CancellationToken cancellationToken = default);

    Task<HonuaAdminEndpointResult<HonuaFormOfflinePolicyResponse>> GetOfflinePolicyAsync(
        string formId,
        CancellationToken cancellationToken = default);

}

public sealed class HonuaFormPackageHttpClient : IHonuaFormPackageClient, IDisposable
{
    private const string AdminBase = "/api/v1/admin/forms/packages";
    private const string RuntimeBase = "/api/v1/forms/packages";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly AdminBodyFormat BodyFormat = new(
        JsonOptions,
        AdminBodyRead.Streamed,
        "The Honua server form package response did not match the expected contract");

    private readonly HttpClient _httpClient;
    private readonly AdminEndpointTransport _transport;

    public HonuaFormPackageHttpClient(HttpClient httpClient, HonuaFormPackageClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);

        BaseUri = options.BaseUri;
        _httpClient = httpClient;
        _httpClient.BaseAddress ??= BaseUri;
        _transport = new AdminEndpointTransport(
            httpClient,
            options.ApiKey,
            AdminTransportFailure.Unreachable("The Honua server form package endpoint"));
    }

    public Uri BaseUri { get; }

    public Task<HonuaAdminEndpointResult<HonuaFormPackageSummary[]>> ListPackagesAsync(
        CancellationToken cancellationToken = default) =>
        SendAsync<HonuaFormPackageSummary[]>(
            () => new HttpRequestMessage(HttpMethod.Get, AdminBase),
            "GET /api/v1/admin/forms/packages",
            cancellationToken);

    public Task<HonuaAdminEndpointResult<HonuaFormPackageVersion>> CreateDraftAsync(
        HonuaFormPackageDocument document,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        return SendAsync<HonuaFormPackageVersion>(
            () => JsonRequest(HttpMethod.Post, AdminBase, document),
            "POST /api/v1/admin/forms/packages",
            cancellationToken);
    }

    public Task<HonuaAdminEndpointResult<HonuaFormPackageVersion>> GetCurrentAsync(
        string formId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(formId);

        return SendAsync<HonuaFormPackageVersion>(
            () => new HttpRequestMessage(HttpMethod.Get, $"{AdminBase}/{Uri.EscapeDataString(formId)}"),
            "GET /api/v1/admin/forms/packages/{formId}",
            cancellationToken);
    }

    public Task<HonuaAdminEndpointResult<HonuaFormPackageVersion[]>> ListVersionsAsync(
        string formId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(formId);

        return SendAsync<HonuaFormPackageVersion[]>(
            () => new HttpRequestMessage(HttpMethod.Get, $"{AdminBase}/{Uri.EscapeDataString(formId)}/versions"),
            "GET /api/v1/admin/forms/packages/{formId}/versions",
            cancellationToken);
    }

    public Task<HonuaAdminEndpointResult<HonuaFormPackageVersion>> GetVersionAsync(
        string formId,
        int packageVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(formId);

        return SendAsync<HonuaFormPackageVersion>(
            () => new HttpRequestMessage(HttpMethod.Get, VersionPath(formId, packageVersion)),
            "GET /api/v1/admin/forms/packages/{formId}/versions/{version}",
            cancellationToken);
    }

    public Task<HonuaAdminEndpointResult<HonuaFormPackageVersion>> UpdateDraftAsync(
        string formId,
        int packageVersion,
        string etag,
        HonuaFormPackageDocument document,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(formId);
        ArgumentException.ThrowIfNullOrWhiteSpace(etag);
        ArgumentNullException.ThrowIfNull(document);

        return SendAsync<HonuaFormPackageVersion>(
            () => DraftUpdateRequest(VersionPath(formId, packageVersion), document, etag),
            "PUT /api/v1/admin/forms/packages/{formId}/versions/{version}",
            cancellationToken);
    }

    public Task<HonuaAdminEndpointResult<HonuaFormPackageValidationResult>> ValidateAsync(
        string formId,
        int packageVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(formId);

        return SendAsync<HonuaFormPackageValidationResult>(
            () => new HttpRequestMessage(HttpMethod.Post, $"{VersionPath(formId, packageVersion)}/validate"),
            "POST /api/v1/admin/forms/packages/{formId}/versions/{version}/validate",
            cancellationToken);
    }

    public Task<HonuaAdminEndpointResult<HonuaFormPackageVersion>> PublishAsync(
        string formId,
        int packageVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(formId);

        var call = new AdminEndpointCall<HonuaFormPackageVersion>(
            () => new HttpRequestMessage(HttpMethod.Post, $"{VersionPath(formId, packageVersion)}/publish"),
            "POST /api/v1/admin/forms/packages/{formId}/versions/{version}/publish",
            ReadPublishRejection(formId, packageVersion),
            AdminResponseBodies.Required<HonuaFormPackageVersion>(BodyFormat, EmptyBody))
        {
            Completion = HttpCompletionOption.ResponseHeadersRead
        };
        return _transport.SendAsync(call, cancellationToken);
    }

    public Task<HonuaAdminEndpointResult<HonuaFormPackageVersion>> ReopenAsync(
        string formId,
        int packageVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(formId);

        return SendAsync<HonuaFormPackageVersion>(
            () => new HttpRequestMessage(HttpMethod.Post, $"{VersionPath(formId, packageVersion)}/reopen"),
            "POST /api/v1/admin/forms/packages/{formId}/versions/{version}/reopen",
            cancellationToken);
    }

    public Task<HonuaAdminEndpointResult<HonuaFormOfflinePolicyResponse>> GetOfflinePolicyAsync(
        string formId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(formId);

        return SendAsync<HonuaFormOfflinePolicyResponse>(
            () => new HttpRequestMessage(HttpMethod.Get, $"{RuntimeBase}/{Uri.EscapeDataString(formId)}/offline-policy"),
            "GET /api/v1/forms/packages/{formId}/offline-policy",
            cancellationToken);
    }

    public void Dispose() => _httpClient.Dispose();

    private const string EmptyBody = "The Honua server form package response body was empty.";

    private static string VersionPath(string formId, int packageVersion) =>
        $"{AdminBase}/{Uri.EscapeDataString(formId)}/versions/{packageVersion.ToString(CultureInfo.InvariantCulture)}";

    private static HttpRequestMessage JsonRequest(HttpMethod method, string path, object body) =>
        new(method, path) { Content = JsonContent.Create(body, body.GetType(), mediaType: null, JsonOptions) };

    private static HttpRequestMessage DraftUpdateRequest(string path, HonuaFormPackageDocument document, string etag)
    {
        var request = JsonRequest(HttpMethod.Put, path, document);
        request.Headers.TryAddWithoutValidation("If-Match", etag);
        return request;
    }

    private Task<HonuaAdminEndpointResult<T>> SendAsync<T>(
        Func<HttpRequestMessage> createRequest,
        string contract,
        CancellationToken cancellationToken) =>
        _transport.SendAsync(
            new AdminEndpointCall<T>(
                createRequest,
                contract,
                AdminFailureMappings.StatusDetail,
                AdminResponseBodies.Required<T>(BodyFormat, EmptyBody))
            {
                Completion = HttpCompletionOption.ResponseHeadersRead
            },
            cancellationToken);

    // A 400 publish rejection carries the validation result; surface it alongside the issue.
    private static AdminResponseReader<HonuaFormPackageVersion> ReadPublishRejection(string formId, int packageVersion) =>
        async (contract, response, cancellationToken) =>
        {
            var statusDetail = AdminFailureMappings.AsReader<HonuaFormPackageVersion>(AdminFailureMappings.StatusDetail);
            if (response.StatusCode != HttpStatusCode.BadRequest)
            {
                return await statusDetail(contract, response, cancellationToken).ConfigureAwait(false);
            }

            string raw;
            HonuaFormPackageValidationResult? validation;
            try
            {
                raw = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                validation = JsonSerializer.Deserialize<HonuaFormPackageValidationResult>(raw, JsonOptions);
            }
            catch (JsonException ex)
            {
                return HonuaAdminEndpointResult<HonuaFormPackageVersion>.FromIssue(new HonuaAdminEndpointIssue(
                    "Unsupported",
                    contract,
                    $"The Honua server publish validation response did not match the expected contract: {ex.Message}",
                    (int)response.StatusCode));
            }

            if (validation is null)
            {
                return await statusDetail(contract, response, cancellationToken).ConfigureAwait(false);
            }

            var version = new HonuaFormPackageVersion { FormId = formId, Version = packageVersion, Validation = validation };
            return new HonuaAdminEndpointResult<HonuaFormPackageVersion>(
                version,
                AdminEndpointIssueFactory.CreateIssue(contract, response, raw));
        };
}

// --- Wire records mirroring honua-server Honua.Core.Features.Forms.Packages (schema honua.form-package.v1). ---

public static class HonuaFormPackageStatus
{
    public const string Draft = "draft";
    public const string Published = "published";
    public const string Archived = "archived";
}

public static class HonuaFormSubmissionOperations
{
    public const string Create = "create";
    public const string Update = "update";
    public const string Delete = "delete";
}

public sealed record HonuaFormPackageDocument
{
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; init; } = "honua.form-package.v1";

    [JsonPropertyName("formId")]
    public string? FormId { get; init; }

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("target")]
    public HonuaFormTargetDefinition? Target { get; init; }

    [JsonPropertyName("sections")]
    public HonuaFormSectionDefinition[] Sections { get; init; } = [];

    [JsonPropertyName("fields")]
    public HonuaFormFieldDefinition[] Fields { get; init; } = [];

    [JsonPropertyName("submitPolicy")]
    public HonuaFormSubmitPolicy SubmitPolicy { get; init; } = new();

    [JsonPropertyName("attachmentPolicy")]
    public HonuaFormAttachmentPolicy AttachmentPolicy { get; init; } = new();

    [JsonPropertyName("privacyPolicy")]
    public HonuaFormPrivacyPolicy PrivacyPolicy { get; init; } = new();

    [JsonPropertyName("offlinePolicy")]
    public HonuaFormOfflinePolicy OfflinePolicy { get; init; } = new();

    [JsonPropertyName("provenance")]
    public HonuaFormProvenanceRef? Provenance { get; init; }

    [JsonPropertyName("metadata")]
    public Dictionary<string, string>? Metadata { get; init; }
}

public sealed record HonuaFormTargetDefinition
{
    [JsonPropertyName("serviceId")]
    public string? ServiceId { get; init; }

    [JsonPropertyName("layerId")]
    public int LayerId { get; init; }
}

public sealed record HonuaFormSectionDefinition
{
    [JsonPropertyName("sectionId")]
    public string? SectionId { get; init; }

    [JsonPropertyName("label")]
    public string? Label { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("fieldIds")]
    public string[] FieldIds { get; init; } = [];
}

public sealed record HonuaFormFieldDefinition
{
    [JsonPropertyName("fieldId")]
    public string? FieldId { get; init; }

    [JsonPropertyName("label")]
    public string? Label { get; init; }

    [JsonPropertyName("hint")]
    public string? Hint { get; init; }

    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("targetField")]
    public string? TargetField { get; init; }

    [JsonPropertyName("sectionId")]
    public string? SectionId { get; init; }

    [JsonPropertyName("required")]
    public bool Required { get; init; }

    [JsonPropertyName("readOnly")]
    public bool ReadOnly { get; init; }

    [JsonPropertyName("private")]
    public bool Private { get; init; }

    [JsonPropertyName("defaultValue")]
    public JsonElement? DefaultValue { get; init; }

    [JsonPropertyName("domain")]
    public HonuaFormFieldDomainDefinition? Domain { get; init; }

    [JsonPropertyName("validation")]
    public HonuaFormValidationRule[] Validation { get; init; } = [];

    [JsonPropertyName("visibility")]
    public HonuaFormConditionalRule? Visibility { get; init; }
}

public sealed record HonuaFormFieldDomainDefinition
{
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("choices")]
    public HonuaFormDomainChoice[] Choices { get; init; } = [];

    [JsonPropertyName("min")]
    public JsonElement? Min { get; init; }

    [JsonPropertyName("max")]
    public JsonElement? Max { get; init; }
}

public sealed record HonuaFormDomainChoice
{
    [JsonPropertyName("code")]
    public JsonElement Code { get; init; }

    [JsonPropertyName("label")]
    public string? Label { get; init; }
}

public sealed record HonuaFormValidationRule
{
    [JsonPropertyName("ruleId")]
    public string? RuleId { get; init; }

    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }

    [JsonPropertyName("parameters")]
    public JsonElement? Parameters { get; init; }
}

public sealed record HonuaFormConditionalRule
{
    [JsonPropertyName("dependsOnFieldId")]
    public string? DependsOnFieldId { get; init; }

    [JsonPropertyName("operator")]
    public string? Operator { get; init; }

    [JsonPropertyName("value")]
    public JsonElement? Value { get; init; }
}

public sealed record HonuaFormSubmitPolicy
{
    [JsonPropertyName("allowedOperations")]
    public string[] AllowedOperations { get; init; } = [HonuaFormSubmissionOperations.Create];

    [JsonPropertyName("requiresGeometry")]
    public bool RequiresGeometry { get; init; } = true;

    [JsonPropertyName("allowAttachments")]
    public bool AllowAttachments { get; init; } = true;

    [JsonPropertyName("maxOfflineAgeSeconds")]
    public int? MaxOfflineAgeSeconds { get; init; }
}

public sealed record HonuaFormAttachmentPolicy
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; }

    [JsonPropertyName("maxAttachmentsPerSubmission")]
    public int? MaxAttachmentsPerSubmission { get; init; }

    [JsonPropertyName("maxAttachmentBytes")]
    public long? MaxAttachmentBytes { get; init; }

    [JsonPropertyName("maxTotalBytes")]
    public long? MaxTotalBytes { get; init; }

    [JsonPropertyName("allowedContentTypes")]
    public string[] AllowedContentTypes { get; init; } = [];

    [JsonPropertyName("fields")]
    public HonuaFormFieldAttachmentPolicy[] Fields { get; init; } = [];

    [JsonPropertyName("requireExifStripping")]
    public bool RequireExifStripping { get; init; }

    [JsonPropertyName("requireFaceBlur")]
    public bool RequireFaceBlur { get; init; }

    [JsonPropertyName("requireRedaction")]
    public bool RequireRedaction { get; init; }
}

public sealed record HonuaFormFieldAttachmentPolicy
{
    [JsonPropertyName("fieldId")]
    public string? FieldId { get; init; }

    [JsonPropertyName("maxCount")]
    public int? MaxCount { get; init; }

    [JsonPropertyName("allowedContentTypes")]
    public string[] AllowedContentTypes { get; init; } = [];

    [JsonPropertyName("required")]
    public bool Required { get; init; }
}

public sealed record HonuaFormPrivacyPolicy
{
    [JsonPropertyName("privateFieldIds")]
    public string[] PrivateFieldIds { get; init; } = [];

    [JsonPropertyName("requiredTransformations")]
    public string[] RequiredTransformations { get; init; } = [];

    [JsonPropertyName("captureActor")]
    public bool CaptureActor { get; init; } = true;

    [JsonPropertyName("captureDeviceId")]
    public bool CaptureDeviceId { get; init; } = true;

    [JsonPropertyName("retentionDays")]
    public int? RetentionDays { get; init; }
}

public sealed record HonuaFormOfflinePolicy
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; }

    [JsonPropertyName("preferredTransports")]
    public string[] PreferredTransports { get; init; } = ["feature-server-replica", "fieldcollection"];

    [JsonPropertyName("replicaTransportEnabled")]
    public bool ReplicaTransportEnabled { get; init; } = true;

    [JsonPropertyName("fieldCollectionTransportEnabled")]
    public bool FieldCollectionTransportEnabled { get; init; } = true;

    [JsonPropertyName("conflictReviewMode")]
    public string? ConflictReviewMode { get; init; } = "defer";
}

public sealed record HonuaFormProvenanceRef
{
    [JsonPropertyName("source")]
    public string? Source { get; init; }

    [JsonPropertyName("sourceVersion")]
    public string? SourceVersion { get; init; }
}

public sealed record HonuaFormPackageVersion
{
    [JsonPropertyName("formId")]
    public string FormId { get; init; } = string.Empty;

    [JsonPropertyName("version")]
    public int Version { get; init; }

    [JsonPropertyName("status")]
    public string Status { get; init; } = HonuaFormPackageStatus.Draft;

    [JsonPropertyName("package")]
    public HonuaFormPackageDocument Package { get; init; } = new();

    [JsonPropertyName("validation")]
    public HonuaFormPackageValidationResult? Validation { get; init; }

    [JsonPropertyName("contentHash")]
    public string ContentHash { get; init; } = string.Empty;

    [JsonPropertyName("policyHash")]
    public string PolicyHash { get; init; } = string.Empty;

    [JsonPropertyName("etag")]
    public string ETag { get; init; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; init; }

    [JsonPropertyName("createdBy")]
    public string? CreatedBy { get; init; }

    [JsonPropertyName("publishedAt")]
    public DateTimeOffset? PublishedAt { get; init; }

    [JsonPropertyName("publishedBy")]
    public string? PublishedBy { get; init; }

    [JsonPropertyName("reopenedFromVersion")]
    public int? ReopenedFromVersion { get; init; }
}

public sealed record HonuaFormPackageSummary
{
    [JsonPropertyName("formId")]
    public string FormId { get; init; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("serviceId")]
    public string ServiceId { get; init; } = string.Empty;

    [JsonPropertyName("layerId")]
    public int LayerId { get; init; }

    [JsonPropertyName("currentDraftVersion")]
    public int? CurrentDraftVersion { get; init; }

    [JsonPropertyName("currentPublishedVersion")]
    public int? CurrentPublishedVersion { get; init; }

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed record HonuaFormPackageValidationResult
{
    [JsonPropertyName("isValid")]
    public bool IsValid { get; init; }

    [JsonPropertyName("issues")]
    public HonuaFormValidationIssue[] Issues { get; init; } = [];
}

public sealed record HonuaFormValidationIssue
{
    [JsonPropertyName("code")]
    public string Code { get; init; } = string.Empty;

    [JsonPropertyName("severity")]
    public string Severity { get; init; } = "error";

    [JsonPropertyName("fieldId")]
    public string? FieldId { get; init; }

    [JsonPropertyName("path")]
    public string? Path { get; init; }

    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;
}

public sealed record HonuaFormOfflinePolicyResponse
{
    [JsonPropertyName("formId")]
    public string FormId { get; init; } = string.Empty;

    [JsonPropertyName("formVersion")]
    public int FormVersion { get; init; }

    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; }

    [JsonPropertyName("serviceId")]
    public string ServiceId { get; init; } = string.Empty;

    [JsonPropertyName("layerId")]
    public int LayerId { get; init; }

    [JsonPropertyName("availableTransports")]
    public string[] AvailableTransports { get; init; } = [];

    [JsonPropertyName("links")]
    public HonuaFormLink[] Links { get; init; } = [];

    [JsonPropertyName("requiredHeaders")]
    public Dictionary<string, string> RequiredHeaders { get; init; } = new(StringComparer.Ordinal);
}

public sealed record HonuaFormLink
{
    [JsonPropertyName("rel")]
    public string Rel { get; init; } = string.Empty;

    [JsonPropertyName("href")]
    public string Href { get; init; } = string.Empty;

    [JsonPropertyName("method")]
    public string Method { get; init; } = "GET";
}
