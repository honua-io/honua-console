using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Honua.Console.Contracts;

public sealed record HonuaContentPublicationClientOptions(Uri BaseUri, string? ApiKey = null);

public interface IHonuaContentPublicationClient
{
    Uri BaseUri { get; }

    Task<HonuaAdminEndpointResult<HonuaContentPublicationDetail>> GetAsync(
        string publicationId,
        CancellationToken cancellationToken = default);

    Task<HonuaAdminEndpointResult<HonuaContentPublicationListPage>> ListAsync(
        string? cursor = null,
        int limit = 100,
        string? lifecycle = null,
        CancellationToken cancellationToken = default);

    Task<HonuaAdminEndpointResult<HonuaContentPublicationVersion>> GetVersionAsync(
        string publicationId,
        string versionSelector,
        CancellationToken cancellationToken = default);

    Task<HonuaAdminEndpointResult<HonuaContentPublicationDetail>> PublishAsync(
        HonuaPublishContentRequest request,
        CancellationToken cancellationToken = default);

    Task<HonuaAdminEndpointResult<HonuaContentPublicationDetail>> RepublishAsync(
        string publicationId,
        HonuaRepublishContentRequest request,
        CancellationToken cancellationToken = default);

    Task<HonuaAdminEndpointResult<HonuaContentPublicationDetail>> RollbackAsync(
        string publicationId,
        HonuaRollbackContentRequest request,
        CancellationToken cancellationToken = default);

    Task<HonuaAdminEndpointResult<HonuaContentPublicationPolicyUpdateResponse>> UpdatePolicyAsync(
        string publicationId,
        HonuaUpdatePublicationPolicyRequest request,
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

    private static readonly AdminBodyFormat BodyFormat = new(
        JsonOptions,
        AdminBodyRead.Streamed,
        "The Honua server content publication response did not match the expected contract");

    private readonly HttpClient _httpClient;
    private readonly AdminEndpointTransport _transport;

    public HonuaContentPublicationHttpClient(HttpClient httpClient, HonuaContentPublicationClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);

        BaseUri = options.BaseUri;
        _httpClient = httpClient;
        _httpClient.BaseAddress ??= BaseUri;
        _transport = new AdminEndpointTransport(
            httpClient,
            options.ApiKey,
            AdminTransportFailure.Unreachable("The Honua server content publication endpoint"));
    }

    public Uri BaseUri { get; }

    public Task<HonuaAdminEndpointResult<HonuaContentPublicationDetail>> GetAsync(
        string publicationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicationId);

        return SendAsync<HonuaContentPublicationDetail>(
            () => new HttpRequestMessage(HttpMethod.Get, $"{Base}/{Uri.EscapeDataString(publicationId)}"),
            "GET /api/v1/console/publications/{publicationId}",
            cancellationToken);
    }

    public Task<HonuaAdminEndpointResult<HonuaContentPublicationListPage>> ListAsync(
        string? cursor = null,
        int limit = 100,
        string? lifecycle = null,
        CancellationToken cancellationToken = default)
    {
        var query = new List<string>
        {
            "limit=" + (limit <= 0 ? 100 : limit).ToString(CultureInfo.InvariantCulture),
        };
        if (!string.IsNullOrEmpty(cursor))
        {
            query.Add("cursor=" + Uri.EscapeDataString(cursor));
        }

        if (!string.IsNullOrWhiteSpace(lifecycle))
        {
            query.Add("lifecycle=" + Uri.EscapeDataString(lifecycle.Trim()));
        }

        return SendAsync<HonuaContentPublicationListPage>(
            () => new HttpRequestMessage(HttpMethod.Get, Base + "?" + string.Join('&', query)),
            "GET /api/v1/console/publications",
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
            () => new HttpRequestMessage(HttpMethod.Get, $"{Base}/{Uri.EscapeDataString(publicationId)}/versions/{Uri.EscapeDataString(versionSelector)}"),
            "GET /api/v1/console/publications/{publicationId}/versions/{versionSelector}",
            cancellationToken);
    }

    public Task<HonuaAdminEndpointResult<HonuaContentPublicationDetail>> PublishAsync(
        HonuaPublishContentRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return SendAsync<HonuaContentPublicationDetail>(
            () => JsonRequest(HttpMethod.Post, Base, request),
            "POST /api/v1/console/publications",
            cancellationToken);
    }

    public Task<HonuaAdminEndpointResult<HonuaContentPublicationDetail>> RepublishAsync(
        string publicationId,
        HonuaRepublishContentRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicationId);
        ArgumentNullException.ThrowIfNull(request);

        return SendAsync<HonuaContentPublicationDetail>(
            () => JsonRequest(HttpMethod.Post, $"{Base}/{Uri.EscapeDataString(publicationId)}/republish", request),
            "POST /api/v1/console/publications/{publicationId}/republish",
            cancellationToken);
    }

    public Task<HonuaAdminEndpointResult<HonuaContentPublicationDetail>> RollbackAsync(
        string publicationId,
        HonuaRollbackContentRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicationId);
        ArgumentNullException.ThrowIfNull(request);

        return SendAsync<HonuaContentPublicationDetail>(
            () => JsonRequest(HttpMethod.Post, $"{Base}/{Uri.EscapeDataString(publicationId)}/rollback", request),
            "POST /api/v1/console/publications/{publicationId}/rollback",
            cancellationToken);
    }

    public Task<HonuaAdminEndpointResult<HonuaContentPublicationPolicyUpdateResponse>> UpdatePolicyAsync(
        string publicationId,
        HonuaUpdatePublicationPolicyRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicationId);
        ArgumentNullException.ThrowIfNull(request);

        return SendAsync<HonuaContentPublicationPolicyUpdateResponse>(
            () => JsonRequest(HttpMethod.Patch, $"{Base}/{Uri.EscapeDataString(publicationId)}/policy", request),
            "PATCH /api/v1/console/publications/{publicationId}/policy",
            cancellationToken);
    }

    public void Dispose() => _httpClient.Dispose();

    private static HttpRequestMessage JsonRequest(HttpMethod method, string path, object body) =>
        new(method, path) { Content = JsonContent.Create(body, body.GetType(), options: JsonOptions) };

    private Task<HonuaAdminEndpointResult<T>> SendAsync<T>(
        Func<HttpRequestMessage> createRequest,
        string contract,
        CancellationToken cancellationToken) =>
        _transport.SendAsync(
            new AdminEndpointCall<T>(
                createRequest,
                contract,
                new(
                    AdminFailureMappings.StatusDetail,
                    AdminResponseBodies.Required<T>(BodyFormat, "The Honua server content publication response body was empty.")))
            {
                Completion = HttpCompletionOption.ResponseHeadersRead
            },
            cancellationToken);
}

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

public sealed record HonuaContentPublicationListPage
{
    [JsonRequired]
    [JsonPropertyName("items")]
    public IReadOnlyList<HonuaContentPublicationDetail> Items { get; init; } = [];

    [JsonPropertyName("total")]
    public int Total { get; init; }

    [JsonPropertyName("nextCursor")]
    public string? NextCursor { get; init; }
}

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

    // The server hashes this payload and does not store the raw document.
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

    // The server hashes this payload and does not store the raw document.
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

    // A mismatch is rejected as 409.
    [JsonPropertyName("expectedEtag")]
    public string? ExpectedEtag { get; init; }
}

public sealed record HonuaRollbackContentRequest
{
    [JsonPropertyName("targetVersionId")]
    public string? TargetVersionId { get; init; }

    [JsonPropertyName("targetRevision")]
    public long? TargetRevision { get; init; }

    // A mismatch is rejected as 409.
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

    // A mismatch is rejected as 409.
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
