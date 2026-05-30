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

    public void Dispose() => _httpClient.Dispose();

    private async Task<HonuaAdminEndpointResult<T>> SendAsync<T>(
        HttpMethod method,
        string path,
        string contract,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
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
                return HonuaAdminEndpointResult<T>.FromIssue(CreateIssue(contract, response.StatusCode));
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

    private static HonuaAdminEndpointIssue CreateIssue(string contract, HttpStatusCode statusCode)
    {
        var state = statusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "Missing permission",
            HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotImplemented => "Unsupported",
            HttpStatusCode.Conflict or HttpStatusCode.PreconditionFailed => "Conflict",
            HttpStatusCode.BadRequest => "Rejected",
            _ => "Unavailable"
        };

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
