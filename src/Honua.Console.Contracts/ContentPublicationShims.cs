using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Honua.Console.Contracts;

// SHIM(honua-server#1183 / honua-sdk-dotnet): honua-server owns the content publication
// registry for Studio-generated artifacts (map/dashboard/report/generated-app). The route
// state plus immutable versions live behind /api/v1/console/publications (ContentPublication*
// records in Honua.Core.Features.Publishing.Content.Domain). honua-sdk-dotnet does not yet
// project these as a consumable stable package, and honua-console wires no SDK NuGet feed
// (see SDK_SHIM_POLICY.md and the form/operate admin shims). Per the Console Patterns Charter
// section 11 and SDK_SHIM_POLICY, the report builder binds the publication read path through a
// thin HttpClient behind this single Honua.Console.Contracts boundary: the wire records below
// mirror the server document graph, and the client speaks the real console lifecycle. Do not add
// a sibling-repo ProjectReference. Swap these for SDK types when honua-sdk-dotnet ships a
// consumable content-publication projection and honua-console#7 wires the feed.
//
// The publication routes are admin routes, so this client reuses the shared
// HonuaAdminEndpointResult<T>/HonuaAdminEndpointIssue envelope (defined in OperateAdminShims.cs).
// The endpoints return the DTO directly rather than a {success,data,message} wrapper, so this
// client deserializes the body straight into the contract type and maps status semantics
// (404 not found, 409 etag conflict) to issues.
public sealed record HonuaContentPublicationClientOptions(Uri BaseUri, string? ApiKey = null);

public interface IHonuaContentPublicationClient
{
    Uri BaseUri { get; }

    /// <summary>Reads a publication's current route state plus its immutable versions (newest first).</summary>
    Task<HonuaAdminEndpointResult<HonuaContentPublicationDetail>> GetAsync(
        string publicationId,
        CancellationToken cancellationToken = default);

    /// <summary>Reads one immutable version of a publication by revision number, "v{n}", or version id.</summary>
    Task<HonuaAdminEndpointResult<HonuaContentPublicationVersion>> GetVersionAsync(
        string publicationId,
        string versionSelector,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes a brand-new immutable artifact version and claims a server-owned route. The server
    /// allocates the publication id, version id, and revision and normalizes the route slug.
    /// </summary>
    Task<HonuaAdminEndpointResult<HonuaContentPublicationDetail>> PublishAsync(
        HonuaPublishContentRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Republishes an existing publication: creates a new immutable version and moves the active route
    /// pointer to it. The previous versions stay immutable for version pinning/rollback.
    /// </summary>
    Task<HonuaAdminEndpointResult<HonuaContentPublicationDetail>> RepublishAsync(
        string publicationId,
        HonuaRepublishContentRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Rolls the active route pointer back to an earlier immutable version. No new version row is created;
    /// the rollback pointer is recorded server-side.
    /// </summary>
    Task<HonuaAdminEndpointResult<HonuaContentPublicationDetail>> RollbackAsync(
        string publicationId,
        HonuaRollbackContentRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the server-owned visibility/share/embed/public-link policy. No new version row is created;
    /// the change is recorded as an audited event and returns the updated route plus any created link.
    /// </summary>
    Task<HonuaAdminEndpointResult<HonuaContentPublicationPolicyUpdateResponse>> UpdatePolicyAsync(
        string publicationId,
        HonuaUpdatePublicationPolicyRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Lists the AI generation providers the server has enabled+configured (UI selector / enablement).</summary>
    Task<HonuaAdminEndpointResult<HonuaReportGenerationProviders>> ListReportGenerationProvidersAsync(
        CancellationToken cancellationToken = default);

    /// <summary>Generates (or refines) a report document from a natural-language prompt.</summary>
    Task<HonuaAdminEndpointResult<HonuaReportGenerationResult>> GenerateReportAsync(
        GenerateReportContentRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates (or refines) a dashboard document from a natural-language prompt. Dashboards share the
    /// <c>POST /api/v1/console/publications/generate</c> endpoint with reports, dispatched server-side on
    /// the request <c>kind</c> ("dashboard"). The request/response shape matches the report contract; the
    /// returned <see cref="HonuaReportGenerationResult.Document"/> is a honua.dashboard-document.v1 payload.
    /// </summary>
    Task<HonuaAdminEndpointResult<HonuaReportGenerationResult>> GenerateDashboardAsync(
        GenerateDashboardContentRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class HonuaContentPublicationHttpClient : IHonuaContentPublicationClient, IDisposable
{
    private const string Base = "/api/v1/console/publications";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly string? _apiKey;

    public HonuaContentPublicationHttpClient(HttpClient httpClient, HonuaContentPublicationClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);

        BaseUri = options.BaseUri;
        _apiKey = options.ApiKey;
        _httpClient = httpClient;
        _httpClient.BaseAddress ??= BaseUri;
    }

    public Uri BaseUri { get; }

    public Task<HonuaAdminEndpointResult<HonuaContentPublicationDetail>> GetAsync(
        string publicationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicationId);

        return SendAsync<HonuaContentPublicationDetail>(
            HttpMethod.Get,
            $"{Base}/{Uri.EscapeDataString(publicationId)}",
            "GET /api/v1/console/publications/{publicationId}",
            cancellationToken);
    }

    public Task<HonuaAdminEndpointResult<HonuaContentPublicationVersion>> GetVersionAsync(
        string publicationId,
        string versionSelector,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(versionSelector);

        return SendAsync<HonuaContentPublicationVersion>(
            HttpMethod.Get,
            $"{Base}/{Uri.EscapeDataString(publicationId)}/versions/{Uri.EscapeDataString(versionSelector)}",
            "GET /api/v1/console/publications/{publicationId}/versions/{versionSelector}",
            cancellationToken);
    }

    public Task<HonuaAdminEndpointResult<HonuaContentPublicationDetail>> PublishAsync(
        HonuaPublishContentRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return SendAsync<HonuaContentPublicationDetail>(
            HttpMethod.Post,
            Base,
            "POST /api/v1/console/publications",
            cancellationToken,
            request);
    }

    public Task<HonuaAdminEndpointResult<HonuaContentPublicationDetail>> RepublishAsync(
        string publicationId,
        HonuaRepublishContentRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicationId);
        ArgumentNullException.ThrowIfNull(request);

        return SendAsync<HonuaContentPublicationDetail>(
            HttpMethod.Post,
            $"{Base}/{Uri.EscapeDataString(publicationId)}/republish",
            "POST /api/v1/console/publications/{publicationId}/republish",
            cancellationToken,
            request);
    }

    public Task<HonuaAdminEndpointResult<HonuaContentPublicationDetail>> RollbackAsync(
        string publicationId,
        HonuaRollbackContentRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicationId);
        ArgumentNullException.ThrowIfNull(request);

        return SendAsync<HonuaContentPublicationDetail>(
            HttpMethod.Post,
            $"{Base}/{Uri.EscapeDataString(publicationId)}/rollback",
            "POST /api/v1/console/publications/{publicationId}/rollback",
            cancellationToken,
            request);
    }

    public Task<HonuaAdminEndpointResult<HonuaContentPublicationPolicyUpdateResponse>> UpdatePolicyAsync(
        string publicationId,
        HonuaUpdatePublicationPolicyRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicationId);
        ArgumentNullException.ThrowIfNull(request);

        return SendAsync<HonuaContentPublicationPolicyUpdateResponse>(
            HttpMethod.Patch,
            $"{Base}/{Uri.EscapeDataString(publicationId)}/policy",
            "PATCH /api/v1/console/publications/{publicationId}/policy",
            cancellationToken,
            request);
    }

    public Task<HonuaAdminEndpointResult<HonuaReportGenerationProviders>> ListReportGenerationProvidersAsync(
        CancellationToken cancellationToken = default) =>
        SendAsync<HonuaReportGenerationProviders>(
            HttpMethod.Get,
            $"{Base}/generation/providers",
            "GET /api/v1/console/publications/generation/providers",
            cancellationToken);

    public Task<HonuaAdminEndpointResult<HonuaReportGenerationResult>> GenerateReportAsync(
        GenerateReportContentRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return SendAsync<HonuaReportGenerationResult>(
            HttpMethod.Post,
            $"{Base}/generate",
            "POST /api/v1/console/publications/generate",
            cancellationToken,
            request);
    }

    public Task<HonuaAdminEndpointResult<HonuaReportGenerationResult>> GenerateDashboardAsync(
        GenerateDashboardContentRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Same endpoint as report generation; the server dispatches on the request kind ("dashboard") and
        // returns the same {status, document, routeSlug, rationale, clarifications, unmappedRequests,
        // capabilityState, provider, model} envelope — only the document is a honua.dashboard-document.v1.
        return SendAsync<HonuaReportGenerationResult>(
            HttpMethod.Post,
            $"{Base}/generate",
            "POST /api/v1/console/publications/generate",
            cancellationToken,
            request);
    }

    public void Dispose() => _httpClient.Dispose();

    private async Task<HonuaAdminEndpointResult<T>> SendAsync<T>(
        HttpMethod method,
        string path,
        string contract,
        CancellationToken cancellationToken,
        object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType(), options: JsonOptions);
        }

        if (!string.IsNullOrWhiteSpace(_apiKey))
        {
            request.Headers.TryAddWithoutValidation("X-API-Key", _apiKey);
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
                $"The Honua server content publication endpoint could not be reached: {ex.Message}"));
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // A TaskCanceledException whose token is not the caller's is an HttpClient timeout, not caller
            // cancellation, so surface it as Unavailable. Caller-requested cancellation is left to propagate
            // (not caught) so it cancels the calling operation instead of being masked as a transport
            // failure — mirrors HonuaFormPackageHttpClient.
            return HonuaAdminEndpointResult<T>.FromIssue(new HonuaAdminEndpointIssue(
                "Unavailable",
                contract,
                $"The Honua server content publication endpoint could not be reached: {ex.Message}"));
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var issue = await AdminEndpointIssueFactory.CreateIssueAsync(contract, response, cancellationToken)
                    .ConfigureAwait(false);
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
                    $"The Honua server content publication response did not match the expected contract: {ex.Message}",
                    (int)response.StatusCode));
            }

            return payload is null
                ? HonuaAdminEndpointResult<T>.FromIssue(new HonuaAdminEndpointIssue(
                    "Unavailable",
                    contract,
                    "The Honua server content publication response body was empty.",
                    (int)response.StatusCode))
                : HonuaAdminEndpointResult<T>.FromData(payload);
        }
    }

    /// <summary>
    /// Best-effort parse of the field-addressable validation errors carried on a 400 response: an RFC-7807
    /// ProblemDetails whose <c>errors</c> extension is an array of the shared <c>FieldValidationError</c>
    /// shape. Never throws — a body that is not JSON, not a problem document, or carries no <c>errors</c>
    /// array yields an empty list so the caller still surfaces the flat HTTP-level issue.
    /// </summary>
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
        // Canonical status→state mapping shared across all admin shims (honua-console#279 PA-239).
        var state = AdminEndpointIssueFactory.MapState(statusCode);

        var detail = statusCode switch
        {
            HttpStatusCode.Unauthorized => "The Honua server rejected the request because admin authentication is missing.",
            HttpStatusCode.Forbidden => "The Honua server rejected the request because the current principal lacks content publication permission.",
            HttpStatusCode.NotFound => "The Honua server content publication or version was not found.",
            HttpStatusCode.MethodNotAllowed => "The Honua server exposes the content publication route but not the required verb.",
            HttpStatusCode.NotImplemented => "The Honua server reports the content publication capability is not implemented.",
            HttpStatusCode.Conflict => "The content publication route state changed; reload before retrying.",
            HttpStatusCode.PreconditionFailed => "The content publication route etag no longer matches; reload before retrying.",
            HttpStatusCode.BadRequest => "The Honua server rejected the content publication request.",
            _ => string.Format(
                CultureInfo.InvariantCulture,
                "The Honua server returned HTTP {0} ({1}) for the content publication request.",
                (int)statusCode,
                statusCode)
        };

        return new HonuaAdminEndpointIssue(state, contract, detail, (int)statusCode);
    }
}

// --- Wire records mirroring honua-server Honua.Core.Features.Publishing.Content.Domain (#1183). ---

public static class HonuaContentPublicationKinds
{
    public const string Map = "map";
    public const string Dashboard = "dashboard";
    public const string Report = "report";
    public const string GeneratedApp = "generated-app";
}

public static class HonuaContentPublicationVisibilities
{
    public const string Private = "private";
    public const string Organization = "organization";
    public const string Team = "team";
    public const string Public = "public";
}

public static class HonuaContentPublicationLifecycles
{
    public const string Active = "active";
    public const string Suspended = "suspended";
    public const string Archived = "archived";
}

public sealed record HonuaContentPublicationBbox
{
    [JsonPropertyName("crs")]
    public string Crs { get; init; } = "EPSG:4326";

    [JsonPropertyName("minX")]
    public double MinX { get; init; }

    [JsonPropertyName("minY")]
    public double MinY { get; init; }

    [JsonPropertyName("maxX")]
    public double MaxX { get; init; }

    [JsonPropertyName("maxY")]
    public double MaxY { get; init; }
}

public sealed record HonuaContentPublicationDependencyRef
{
    [JsonPropertyName("kind")]
    public string Kind { get; init; } = string.Empty;

    [JsonPropertyName("refId")]
    public string RefId { get; init; } = string.Empty;

    [JsonPropertyName("revision")]
    public long? Revision { get; init; }

    [JsonPropertyName("etag")]
    public string? Etag { get; init; }

    [JsonPropertyName("label")]
    public string? Label { get; init; }
}

public sealed record HonuaContentPublicationProvenanceRef
{
    [JsonPropertyName("kind")]
    public string Kind { get; init; } = string.Empty;

    [JsonPropertyName("refId")]
    public string RefId { get; init; } = string.Empty;

    [JsonPropertyName("rel")]
    public string? Rel { get; init; }
}

public sealed record HonuaContentSharePolicy
{
    [JsonPropertyName("allowSharing")]
    public bool AllowSharing { get; init; }

    [JsonPropertyName("allowAnonymous")]
    public bool AllowAnonymous { get; init; }

    [JsonPropertyName("allowedScopes")]
    public string[]? AllowedScopes { get; init; }
}

public sealed record HonuaContentEmbedPolicy
{
    [JsonPropertyName("allowEmbedding")]
    public bool AllowEmbedding { get; init; }

    [JsonPropertyName("allowedOrigins")]
    public string[]? AllowedOrigins { get; init; }

    [JsonPropertyName("frameAncestors")]
    public string[]? FrameAncestors { get; init; }
}

public sealed record HonuaContentServicePolicy
{
    [JsonPropertyName("requireAuthenticatedServices")]
    public bool RequireAuthenticatedServices { get; init; }

    [JsonPropertyName("allowedServiceIds")]
    public string[]? AllowedServiceIds { get; init; }
}

public sealed record HonuaContentPublicationPolicy
{
    [JsonPropertyName("visibility")]
    public string Visibility { get; init; } = HonuaContentPublicationVisibilities.Private;

    [JsonPropertyName("share")]
    public HonuaContentSharePolicy Share { get; init; } = new();

    [JsonPropertyName("embed")]
    public HonuaContentEmbedPolicy Embed { get; init; } = new();

    [JsonPropertyName("service")]
    public HonuaContentServicePolicy Service { get; init; } = new();
}

public sealed record HonuaContentPublicationVersion
{
    [JsonPropertyName("publicationId")]
    public string PublicationId { get; init; } = string.Empty;

    [JsonPropertyName("versionId")]
    public string VersionId { get; init; } = string.Empty;

    [JsonPropertyName("revision")]
    public long Revision { get; init; }

    [JsonPropertyName("kind")]
    public string Kind { get; init; } = string.Empty;

    [JsonPropertyName("routeSlug")]
    public string RouteSlug { get; init; } = string.Empty;

    [JsonPropertyName("routePath")]
    public string RoutePath { get; init; } = string.Empty;

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("sourceContentId")]
    public string? SourceContentId { get; init; }

    [JsonPropertyName("sourcePackageId")]
    public string? SourcePackageId { get; init; }

    [JsonPropertyName("contentHash")]
    public string? ContentHash { get; init; }

    [JsonPropertyName("contentVersionId")]
    public string? ContentVersionId { get; init; }

    [JsonPropertyName("defaultViewBbox")]
    public HonuaContentPublicationBbox? DefaultViewBbox { get; init; }

    [JsonPropertyName("policy")]
    public HonuaContentPublicationPolicy Policy { get; init; } = new();

    [JsonPropertyName("dependencies")]
    public HonuaContentPublicationDependencyRef[] Dependencies { get; init; } = [];

    [JsonPropertyName("provenance")]
    public HonuaContentPublicationProvenanceRef[] Provenance { get; init; } = [];

    [JsonPropertyName("createdBy")]
    public string CreatedBy { get; init; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; init; }
}

public sealed record HonuaContentPublicationRouteState
{
    [JsonPropertyName("publicationId")]
    public string PublicationId { get; init; } = string.Empty;

    [JsonPropertyName("routeSlug")]
    public string RouteSlug { get; init; } = string.Empty;

    [JsonPropertyName("routePath")]
    public string RoutePath { get; init; } = string.Empty;

    [JsonPropertyName("kind")]
    public string Kind { get; init; } = string.Empty;

    [JsonPropertyName("activeVersionId")]
    public string ActiveVersionId { get; init; } = string.Empty;

    [JsonPropertyName("activeRevision")]
    public long ActiveRevision { get; init; }

    [JsonPropertyName("previousVersionId")]
    public string? PreviousVersionId { get; init; }

    [JsonPropertyName("rollbackTargetVersionId")]
    public string? RollbackTargetVersionId { get; init; }

    [JsonPropertyName("lifecycle")]
    public string Lifecycle { get; init; } = HonuaContentPublicationLifecycles.Active;

    [JsonPropertyName("policy")]
    public HonuaContentPublicationPolicy Policy { get; init; } = new();

    [JsonPropertyName("generation")]
    public long Generation { get; init; }

    [JsonPropertyName("etag")]
    public string Etag { get; init; } = string.Empty;

    [JsonPropertyName("updatedBy")]
    public string UpdatedBy { get; init; } = string.Empty;

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; init; }

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; init; }
}

public sealed record HonuaContentPublicationDetail
{
    [JsonPropertyName("route")]
    public HonuaContentPublicationRouteState Route { get; init; } = new();

    [JsonPropertyName("versions")]
    public HonuaContentPublicationVersion[] Versions { get; init; } = [];
}

// --- Mutation request/response records mirroring ContentPublicationRequests.cs (#1183). ---
//
// Kinds/visibilities are sent as the server's lowercase enum member names (the server enums use
// [JsonStringEnumMemberName]); the constants in HonuaContentPublication* above carry those exact wire
// strings, so the report builder sends and receives the same tokens without an enum projection here.

public sealed record HonuaPublishContentRequest
{
    [JsonPropertyName("kind")]
    public string Kind { get; init; } = HonuaContentPublicationKinds.Report;

    [JsonPropertyName("routeSlug")]
    public string? RouteSlug { get; init; }

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("sourceContentId")]
    public string? SourceContentId { get; init; }

    [JsonPropertyName("sourcePackageId")]
    public string? SourcePackageId { get; init; }

    /// <summary>Raw report-document payload; the server hashes it (SHA-256) and never stores it.</summary>
    [JsonPropertyName("contentPayload")]
    public string? ContentPayload { get; init; }

    [JsonPropertyName("contentHash")]
    public string? ContentHash { get; init; }

    [JsonPropertyName("contentVersionId")]
    public string? ContentVersionId { get; init; }

    [JsonPropertyName("defaultViewBbox")]
    public HonuaContentPublicationBbox? DefaultViewBbox { get; init; }

    [JsonPropertyName("policy")]
    public HonuaContentPublicationPolicy? Policy { get; init; }

    [JsonPropertyName("dependencies")]
    public HonuaContentPublicationDependencyRef[]? Dependencies { get; init; }

    [JsonPropertyName("provenance")]
    public HonuaContentPublicationProvenanceRef[]? Provenance { get; init; }
}

public sealed record HonuaRepublishContentRequest
{
    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("sourceContentId")]
    public string? SourceContentId { get; init; }

    [JsonPropertyName("sourcePackageId")]
    public string? SourcePackageId { get; init; }

    [JsonPropertyName("contentPayload")]
    public string? ContentPayload { get; init; }

    [JsonPropertyName("contentHash")]
    public string? ContentHash { get; init; }

    [JsonPropertyName("contentVersionId")]
    public string? ContentVersionId { get; init; }

    [JsonPropertyName("defaultViewBbox")]
    public HonuaContentPublicationBbox? DefaultViewBbox { get; init; }

    [JsonPropertyName("dependencies")]
    public HonuaContentPublicationDependencyRef[]? Dependencies { get; init; }

    [JsonPropertyName("provenance")]
    public HonuaContentPublicationProvenanceRef[]? Provenance { get; init; }

    [JsonPropertyName("jobId")]
    public string? JobId { get; init; }

    /// <summary>Expected route etag for optimistic concurrency. A mismatch surfaces as a 409 Conflict.</summary>
    [JsonPropertyName("expectedEtag")]
    public string? ExpectedEtag { get; init; }
}

public sealed record HonuaRollbackContentRequest
{
    [JsonPropertyName("targetVersionId")]
    public string? TargetVersionId { get; init; }

    [JsonPropertyName("targetRevision")]
    public long? TargetRevision { get; init; }

    [JsonPropertyName("expectedEtag")]
    public string? ExpectedEtag { get; init; }
}

public sealed record HonuaUpdatePublicationPolicyRequest
{
    [JsonPropertyName("visibility")]
    public string? Visibility { get; init; }

    [JsonPropertyName("share")]
    public HonuaContentSharePolicy? Share { get; init; }

    [JsonPropertyName("embed")]
    public HonuaContentEmbedPolicy? Embed { get; init; }

    [JsonPropertyName("service")]
    public HonuaContentServicePolicy? Service { get; init; }

    [JsonPropertyName("publicLinkEnabled")]
    public bool? PublicLinkEnabled { get; init; }

    [JsonPropertyName("expectedEtag")]
    public string? ExpectedEtag { get; init; }
}

public sealed record HonuaContentPublicationPolicyUpdateResponse
{
    [JsonPropertyName("route")]
    public HonuaContentPublicationRouteState Route { get; init; } = new();

    [JsonPropertyName("createdPublicLinkId")]
    public string? CreatedPublicLinkId { get; init; }

    [JsonPropertyName("createdPublicLinkToken")]
    public string? CreatedPublicLinkToken { get; init; }
}

#region AI generation DTOs (report-generation)

// Wire contract for natural-language -> report.document generation. The honua-server side is the FROZEN spec
// the server implements (mirrors honua-server/docs/design/ai-workflow-generation.md): a provider-pluggable
// planner (local GIS model default, Claude + GPT options) that proposes a honua.report-document.v1 payload
// grounded in the available content bindings and validated before returning, exposed as the two console
// endpoints below. Until the server ships these endpoints they return 404 -> the console renders the honest
// "AI generation unavailable" state (no fabricated report). camelCase props.
//
//   GET  /api/v1/console/publications/generation/providers  -> HonuaReportGenerationProviders
//   POST /api/v1/console/publications/generate              -> HonuaReportGenerationResult (kind=report)
//
// The generate endpoint reuses the shared HonuaAdminEndpointResult envelope like the rest of this client.

/// <summary>A selectable generation provider advertised by the server (only configured+enabled ones appear).</summary>
public sealed record HonuaReportGenerationProviderInfo
{
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;
    [JsonPropertyName("label")] public string Label { get; init; } = string.Empty;
    /// <summary>"local" | "anthropic" | "openai" | "deterministic".</summary>
    [JsonPropertyName("kind")] public string Kind { get; init; } = string.Empty;
    [JsonPropertyName("available")] public bool Available { get; init; }
    [JsonPropertyName("detail")] public string? Detail { get; init; }
}

/// <summary>Result of GET .../generation/providers: whether AI generation is on, and which providers.</summary>
public sealed record HonuaReportGenerationProviders
{
    [JsonPropertyName("enabled")] public bool Enabled { get; init; }
    [JsonPropertyName("defaultProvider")] public string? DefaultProvider { get; init; }
    [JsonPropertyName("providers")] public HonuaReportGenerationProviderInfo[] Providers { get; init; } = [];
}

public sealed record HonuaReportGenerationClarificationChoice
{
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;
    [JsonPropertyName("label")] public string Label { get; init; } = string.Empty;
    [JsonPropertyName("effect")] public string? Effect { get; init; }
}

/// <summary>A structured question the planner needs answered before it can propose a report. Rendered as cards.</summary>
public sealed record HonuaReportGenerationClarification
{
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;
    /// <summary>"binding" | "panel" | "chart" | "visibility" | ...</summary>
    [JsonPropertyName("kind")] public string Kind { get; init; } = string.Empty;
    [JsonPropertyName("prompt")] public string Prompt { get; init; } = string.Empty;
    [JsonPropertyName("reason")] public string? Reason { get; init; }
    [JsonPropertyName("choices")] public HonuaReportGenerationClarificationChoice[] Choices { get; init; } = [];
}

/// <summary>AI-builder capability state (supported/degraded/unsupported/auth_denied/oversized).</summary>
public sealed record HonuaReportGenerationCapabilityState
{
    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
    [JsonPropertyName("state")] public string State { get; init; } = string.Empty;
    [JsonPropertyName("reason")] public string? Reason { get; init; }
}

public sealed record HonuaReportGenerationUsage
{
    [JsonPropertyName("promptTokens")] public int? PromptTokens { get; init; }
    [JsonPropertyName("completionTokens")] public int? CompletionTokens { get; init; }
    [JsonPropertyName("latencyMs")] public int? LatencyMs { get; init; }
}

/// <summary>Result of POST .../generate. <c>status</c> mirrors the plan-analysis statuses:
/// "generated" | "needs-clarification" | "unsupported" | "refused" | "error".</summary>
public sealed record HonuaReportGenerationResult
{
    [JsonPropertyName("status")] public string Status { get; init; } = string.Empty;
    /// <summary>The proposed honua.report-document.v1 payload (same shape the publish contentPayload accepts),
    /// as a raw JSON document. Present iff status=="generated". The console deserializes it into editor state
    /// via the same StudioReportDocument round-trip the builder uses.</summary>
    [JsonPropertyName("document")] public JsonElement? Document { get; init; }
    /// <summary>Suggested route slug for the proposed report (optional).</summary>
    [JsonPropertyName("routeSlug")] public string? RouteSlug { get; init; }
    [JsonPropertyName("rationale")] public string? Rationale { get; init; }
    [JsonPropertyName("clarifications")] public HonuaReportGenerationClarification[] Clarifications { get; init; } = [];
    /// <summary>Things the prompt asked for with no matching binding/panel (grounding gaps), surfaced not dropped.</summary>
    [JsonPropertyName("unmappedRequests")] public string[] UnmappedRequests { get; init; } = [];
    [JsonPropertyName("capabilityState")] public HonuaReportGenerationCapabilityState? CapabilityState { get; init; }
    [JsonPropertyName("provider")] public string? Provider { get; init; }
    [JsonPropertyName("model")] public string? Model { get; init; }
    [JsonPropertyName("usage")] public HonuaReportGenerationUsage? Usage { get; init; }
}

public sealed record HonuaReportGenerationAnswer
{
    [JsonPropertyName("questionId")] public string QuestionId { get; init; } = string.Empty;
    [JsonPropertyName("optionId")] public string OptionId { get; init; } = string.Empty;
}

public sealed record HonuaReportGenerationTurn
{
    /// <summary>"user" | "assistant".</summary>
    [JsonPropertyName("role")] public string Role { get; init; } = string.Empty;
    [JsonPropertyName("content")] public string Content { get; init; } = string.Empty;
}

public sealed record GenerateReportContentRequest
{
    /// <summary>Always "report" for the report builder; the endpoint is shared with other content kinds.</summary>
    [JsonPropertyName("kind")] public string Kind { get; init; } = HonuaContentPublicationKinds.Report;
    [JsonPropertyName("prompt")] public string Prompt { get; init; } = string.Empty;
    /// <summary>Provider id to use; null selects the server default.</summary>
    [JsonPropertyName("provider")] public string? Provider { get; init; }
    /// <summary>Optional per-call model override.</summary>
    [JsonPropertyName("model")] public string? Model { get; init; }
    /// <summary>Current report-document payload for a REFINE turn; null requests fresh generation.</summary>
    [JsonPropertyName("document")] public JsonElement? Document { get; init; }
    [JsonPropertyName("conversation")] public HonuaReportGenerationTurn[] Conversation { get; init; } = [];
    /// <summary>Answers to a prior needs-clarification turn.</summary>
    [JsonPropertyName("answers")] public HonuaReportGenerationAnswer[] Answers { get; init; } = [];
}

/// <summary>
/// Wire request for natural-language -> dashboard.document generation. Dashboards share the
/// <c>publications/generate</c> endpoint with reports, dispatched server-side on <c>kind</c>; the shape
/// matches <see cref="GenerateReportContentRequest"/> with the kind pinned to "dashboard". The optional
/// <c>document</c> carries a current honua.dashboard-document.v1 payload for a REFINE turn.
/// </summary>
public sealed record GenerateDashboardContentRequest
{
    /// <summary>Always "dashboard" for the dashboard builder; the endpoint is shared with other content kinds.</summary>
    [JsonPropertyName("kind")] public string Kind { get; init; } = HonuaContentPublicationKinds.Dashboard;
    [JsonPropertyName("prompt")] public string Prompt { get; init; } = string.Empty;
    /// <summary>Provider id to use; null selects the server default.</summary>
    [JsonPropertyName("provider")] public string? Provider { get; init; }
    /// <summary>Optional per-call model override.</summary>
    [JsonPropertyName("model")] public string? Model { get; init; }
    /// <summary>Current dashboard-document payload for a REFINE turn; null requests fresh generation.</summary>
    [JsonPropertyName("document")] public JsonElement? Document { get; init; }
    [JsonPropertyName("conversation")] public HonuaReportGenerationTurn[] Conversation { get; init; } = [];
    /// <summary>Answers to a prior needs-clarification turn.</summary>
    [JsonPropertyName("answers")] public HonuaReportGenerationAnswer[] Answers { get; init; } = [];
}

#endregion
