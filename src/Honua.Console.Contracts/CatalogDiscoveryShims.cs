using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Honua.Console.Contracts;

// SHIM(honua-server#1279): honua-server owns the catalog discovery-endpoints registry — the set of
// discovery dialects a workspace exposes to consumers (Esri catalog, OGC API Records, OData, STAC, DCAT),
// each endpoint's server-wide on/off state, whether it is auto-default-on or opt-in, the feeders that
// populate it (FeatureServer / MapServer / ImageServer / OGC API Features / OData entity sets), its entry
// count and per-endpoint validation issues, and the per-item drill-down (one catalog item with its
// resource-derived identity, catalog-only presentation, service bindings, and standards-mapping records).
// This is DISTINCT from the singular content catalog read surface (honua-server#1162,
// /api/v1/console/content) that backs /catalog: the discovery-endpoints registry is the operator surface
// for which catalog dialects the server publishes, not the content items themselves.
//
// honua-server does not yet expose a Console catalog discovery-endpoints registry contract (verified
// against Honua.Server.Features.Console — only content, share, and workflow-package routes exist). Per the
// Console Patterns Charter section 11 and SDK_SHIM_POLICY, the wire records below describe the expected
// server projection so the Console surface can bind a thin HttpClient behind this single
// Honua.Console.Contracts boundary the moment honua-server#1279 lands. Until then the Console Catalogs
// surface renders an explicit missing-binding state rather than fabricating endpoint/item data, and no
// sibling-repo ProjectReference is added. Swap these for SDK types when honua-sdk-dotnet ships a consumable
// projection.
//
// The reads are admin reads wrapped in the shared {success,data,message,timestamp} envelope
// (Honua.Infrastructure.Models.ApiResponse<T>), so this client reuses the shared HonuaAdminApiResponse<T>
// envelope and the HonuaAdminEndpointResult<T>/HonuaAdminEndpointIssue records (defined in
// OperateAdminShims.cs).

/// <summary>The catalog discovery dialects the registry can expose (mirrors honua-server CatalogDialect).</summary>
public static class HonuaCatalogDialects
{
    public const string Esri = "esri";
    public const string Ogc = "ogc";
    public const string ODataV4 = "odata";
    public const string Stac = "stac";
    public const string Dcat = "dcat";
}

public sealed record HonuaCatalogDiscoveryClientOptions(Uri BaseUri, string? ApiKey = null);

/// <summary>
/// Typed client for the honua-server catalog discovery-endpoints registry (honua-server#1279). Reads target
/// the workspace-scoped admin routes under <c>/api/v1/console/catalog-endpoints</c>.
/// </summary>
public interface IHonuaCatalogDiscoveryClient
{
    Uri BaseUri { get; }

    /// <summary>Reads the server-owned discovery-endpoints registry (the CatalogsList surface).</summary>
    Task<HonuaAdminEndpointResult<HonuaCatalogDiscoveryRegistry>> GetRegistryAsync(
        string workspaceId,
        CancellationToken cancellationToken = default);

    /// <summary>Reads one discovery endpoint with its mirrored items (the EsriCatalogDetail drill-down).</summary>
    Task<HonuaAdminEndpointResult<HonuaCatalogEndpointDetail>> GetEndpointAsync(
        string workspaceId,
        string endpointKey,
        CancellationToken cancellationToken = default);

    /// <summary>Reads one catalog item with its derived/catalog-only/standards-mapping facets (CatalogItemEditor).</summary>
    Task<HonuaAdminEndpointResult<HonuaCatalogItem>> GetItemAsync(
        string workspaceId,
        string endpointKey,
        string itemId,
        CancellationToken cancellationToken = default);
}

public sealed class HonuaCatalogDiscoveryHttpClient : IHonuaCatalogDiscoveryClient, IDisposable
{
    private const string Base = "/api/v1/console/catalog-endpoints";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly string? _apiKey;

    public HonuaCatalogDiscoveryHttpClient(HttpClient httpClient, HonuaCatalogDiscoveryClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);

        BaseUri = options.BaseUri;
        _apiKey = options.ApiKey;
        _httpClient = httpClient;
        _httpClient.BaseAddress ??= BaseUri;
    }

    public Uri BaseUri { get; }

    public Task<HonuaAdminEndpointResult<HonuaCatalogDiscoveryRegistry>> GetRegistryAsync(
        string workspaceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);

        return SendAsync<HonuaCatalogDiscoveryRegistry>(
            $"{Base}/{Uri.EscapeDataString(workspaceId)}",
            "GET /api/v1/console/catalog-endpoints/{workspaceId}",
            cancellationToken);
    }

    public Task<HonuaAdminEndpointResult<HonuaCatalogEndpointDetail>> GetEndpointAsync(
        string workspaceId,
        string endpointKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(endpointKey);

        return SendAsync<HonuaCatalogEndpointDetail>(
            $"{Base}/{Uri.EscapeDataString(workspaceId)}/{Uri.EscapeDataString(endpointKey)}",
            "GET /api/v1/console/catalog-endpoints/{workspaceId}/{endpointKey}",
            cancellationToken);
    }

    public Task<HonuaAdminEndpointResult<HonuaCatalogItem>> GetItemAsync(
        string workspaceId,
        string endpointKey,
        string itemId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(endpointKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);

        return SendAsync<HonuaCatalogItem>(
            $"{Base}/{Uri.EscapeDataString(workspaceId)}/{Uri.EscapeDataString(endpointKey)}/items/{Uri.EscapeDataString(itemId)}",
            "GET /api/v1/console/catalog-endpoints/{workspaceId}/{endpointKey}/items/{itemId}",
            cancellationToken);
    }

    public void Dispose() => _httpClient.Dispose();

    private async Task<HonuaAdminEndpointResult<T>> SendAsync<T>(
        string path,
        string contract,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
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
            return Unavailable<T>(contract, ex.Message);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            return Unavailable<T>(contract, ex.Message);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var state = response.StatusCode switch
                {
                    System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden => "Missing permission",
                    System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.MethodNotAllowed or System.Net.HttpStatusCode.NotImplemented => "Unsupported",
                    _ => "Unavailable"
                };
                var detail = response.StatusCode switch
                {
                    System.Net.HttpStatusCode.Unauthorized => "The Honua server rejected the request because admin authentication is missing.",
                    System.Net.HttpStatusCode.Forbidden => "The current principal lacks permission to read catalog discovery endpoints.",
                    System.Net.HttpStatusCode.NotFound => "The Honua server does not expose the catalog discovery-endpoints registry (honua-server#1279).",
                    _ => $"The Honua server returned HTTP {(int)response.StatusCode} ({response.StatusCode})."
                };
                return HonuaAdminEndpointResult<T>.FromIssue(
                    new HonuaAdminEndpointIssue(state, contract, detail, (int)response.StatusCode));
            }

            HonuaAdminApiResponse<T>? envelope;
            try
            {
                envelope = await response.Content
                    .ReadFromJsonAsync<HonuaAdminApiResponse<T>>(JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (JsonException ex)
            {
                return HonuaAdminEndpointResult<T>.FromIssue(new HonuaAdminEndpointIssue(
                    "Unsupported",
                    contract,
                    $"The Honua server catalog discovery response did not match the expected contract: {ex.Message}",
                    (int)response.StatusCode));
            }

            if (envelope?.Success == true && envelope.Data is not null)
            {
                return HonuaAdminEndpointResult<T>.FromData(envelope.Data);
            }

            return HonuaAdminEndpointResult<T>.FromIssue(new HonuaAdminEndpointIssue(
                "Unavailable",
                contract,
                envelope?.Message ?? "The Honua server catalog discovery response did not include data.",
                (int)response.StatusCode));
        }
    }

    private static HonuaAdminEndpointResult<T> Unavailable<T>(string contract, string detail) =>
        HonuaAdminEndpointResult<T>.FromIssue(new HonuaAdminEndpointIssue(
            "Unavailable",
            contract,
            $"The Honua server catalog discovery endpoint could not be reached: {detail}"));
}

// --- Wire records mirroring the expected honua-server projection (#1279). ---

/// <summary>One feeder that populates a discovery endpoint (e.g. a FeatureServer or an OGC API collection).</summary>
public sealed record HonuaCatalogFeeder
{
    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    [JsonPropertyName("label")]
    public required string Label { get; init; }
}

/// <summary>One discovery endpoint card in the registry: dialect, on/off, auto-default vs opt-in, feeders, issues.</summary>
public sealed record HonuaCatalogEndpoint
{
    [JsonPropertyName("key")]
    public required string Key { get; init; }

    [JsonPropertyName("title")]
    public required string Title { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("dialect")]
    public required string Dialect { get; init; }

    /// <summary>Server-wide on/off state (Settings → Catalog endpoints).</summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; }

    /// <summary>True when the endpoint auto-mirrors publications (auto-default-on); false when resources opt in.</summary>
    [JsonPropertyName("autoDefault")]
    public bool AutoDefault { get; init; }

    [JsonPropertyName("url")]
    public string? Url { get; init; }

    [JsonPropertyName("entries")]
    public int Entries { get; init; }

    [JsonPropertyName("fedBy")]
    public string? FedBy { get; init; }

    [JsonPropertyName("feeders")]
    public IReadOnlyList<HonuaCatalogFeeder> Feeders { get; init; } = Array.Empty<HonuaCatalogFeeder>();

    [JsonPropertyName("issueCount")]
    public int IssueCount { get; init; }
}

/// <summary>The discovery-endpoints registry projection: the endpoint cards plus auto/opt-in aggregate counts.</summary>
public sealed record HonuaCatalogDiscoveryRegistry
{
    [JsonPropertyName("workspaceId")]
    public required string WorkspaceId { get; init; }

    [JsonPropertyName("workspaceName")]
    public string? WorkspaceName { get; init; }

    [JsonPropertyName("publicHost")]
    public string? PublicHost { get; init; }

    [JsonPropertyName("endpoints")]
    public IReadOnlyList<HonuaCatalogEndpoint> Endpoints { get; init; } = Array.Empty<HonuaCatalogEndpoint>();

    [JsonPropertyName("autoDefaultCount")]
    public int AutoDefaultCount { get; init; }

    [JsonPropertyName("optInCount")]
    public int OptInCount { get; init; }
}

/// <summary>One row in the EsriCatalogDetail items table: a mirrored publication item.</summary>
public sealed record HonuaCatalogEndpointItem
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("title")]
    public required string Title { get; init; }

    [JsonPropertyName("fromService")]
    public string? FromService { get; init; }

    [JsonPropertyName("resource")]
    public string? Resource { get; init; }

    [JsonPropertyName("tags")]
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();

    [JsonPropertyName("hasThumbnail")]
    public bool HasThumbnail { get; init; }

    [JsonPropertyName("license")]
    public string? License { get; init; }

    [JsonPropertyName("issueCount")]
    public int IssueCount { get; init; }

    [JsonPropertyName("updated")]
    public string? Updated { get; init; }
}

/// <summary>The EsriCatalogDetail projection: the endpoint header facts plus the mirrored items table.</summary>
public sealed record HonuaCatalogEndpointDetail
{
    [JsonPropertyName("endpoint")]
    public required HonuaCatalogEndpoint Endpoint { get; init; }

    [JsonPropertyName("lastRebuild")]
    public string? LastRebuild { get; init; }

    [JsonPropertyName("autoMirror")]
    public bool AutoMirror { get; init; }

    [JsonPropertyName("items")]
    public IReadOnlyList<HonuaCatalogEndpointItem> Items { get; init; } = Array.Empty<HonuaCatalogEndpointItem>();
}

/// <summary>How a single catalog-item field is sourced: derived from the resource, system, or catalog-only input.</summary>
public static class HonuaCatalogFieldStates
{
    public const string System = "system";
    public const string Calculated = "calculated";
    public const string Input = "input";
}

/// <summary>One field in the CatalogItemEditor with its source state and (for derived fields) its origin.</summary>
public sealed record HonuaCatalogItemField
{
    [JsonPropertyName("label")]
    public required string Label { get; init; }

    [JsonPropertyName("state")]
    public required string State { get; init; }

    [JsonPropertyName("value")]
    public string? Value { get; init; }

    [JsonPropertyName("derivedFrom")]
    public string? DerivedFrom { get; init; }
}

/// <summary>A field group in the CatalogItemEditor (Identity, Catalog presentation, Service bindings, Standards mapping).</summary>
public sealed record HonuaCatalogItemFieldGroup
{
    [JsonPropertyName("title")]
    public required string Title { get; init; }

    [JsonPropertyName("subtitle")]
    public string? Subtitle { get; init; }

    [JsonPropertyName("scope")]
    public string? Scope { get; init; }

    [JsonPropertyName("fields")]
    public IReadOnlyList<HonuaCatalogItemField> Fields { get; init; } = Array.Empty<HonuaCatalogItemField>();
}

/// <summary>The CatalogItemEditor projection: an auto-mirrored item with its derived/catalog/binding/standards groups.</summary>
public sealed record HonuaCatalogItem
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("title")]
    public required string Title { get; init; }

    [JsonPropertyName("autoMirror")]
    public bool AutoMirror { get; init; }

    [JsonPropertyName("live")]
    public bool Live { get; init; }

    [JsonPropertyName("backingServiceCount")]
    public int BackingServiceCount { get; init; }

    [JsonPropertyName("tagCount")]
    public int TagCount { get; init; }

    [JsonPropertyName("issueCount")]
    public int IssueCount { get; init; }

    [JsonPropertyName("groups")]
    public IReadOnlyList<HonuaCatalogItemFieldGroup> Groups { get; init; } = Array.Empty<HonuaCatalogItemFieldGroup>();
}
