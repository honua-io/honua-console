using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Honua.Console.Contracts;

public static class HonuaCatalogDialects
{
    public const string Esri = "esri";
    public const string Ogc = "ogc";
    public const string ODataV4 = "odata";
    public const string Stac = "stac";
    public const string Dcat = "dcat";
}

public sealed record HonuaCatalogDiscoveryClientOptions(Uri BaseUri, string? ApiKey = null);

public interface IHonuaCatalogDiscoveryClient
{
    Uri BaseUri { get; }

    Task<HonuaAdminEndpointResult<HonuaCatalogDiscoveryRegistry>> GetRegistryAsync(
        string workspaceId,
        CancellationToken cancellationToken = default);

    Task<HonuaAdminEndpointResult<HonuaCatalogEndpointDetail>> GetEndpointAsync(
        string workspaceId,
        string endpointKey,
        CancellationToken cancellationToken = default);

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

    private static readonly AdminBodyFormat EnvelopeFormat = new(
        JsonOptions,
        AdminBodyRead.Streamed,
        "The Honua server catalog discovery response did not match the expected contract");

    private readonly HttpClient _httpClient;
    private readonly AdminEndpointTransport _transport;

    public HonuaCatalogDiscoveryHttpClient(HttpClient httpClient, HonuaCatalogDiscoveryClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);

        BaseUri = options.BaseUri;
        _httpClient = httpClient;
        _httpClient.BaseAddress ??= BaseUri;
        _transport = new AdminEndpointTransport(
            httpClient,
            options.ApiKey,
            AdminTransportFailure.Unreachable("The Honua server catalog discovery endpoint"));
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

    private Task<HonuaAdminEndpointResult<T>> SendAsync<T>(
        string path,
        string contract,
        CancellationToken cancellationToken) =>
        _transport.SendAsync(
            new AdminEndpointCall<T>(
                () => new HttpRequestMessage(HttpMethod.Get, path),
                contract,
                new(
                    AdminFailureMappings.FromStatus(CreateIssue),
                    AdminResponseBodies.Envelope<T>(EnvelopeFormat, "The Honua server catalog discovery response did not include data.")))
            {
                Completion = HttpCompletionOption.ResponseHeadersRead
            },
            cancellationToken);

    private static HonuaAdminEndpointIssue CreateIssue(string contract, HttpStatusCode statusCode)
    {
        var state = statusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "Missing permission",
            HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotImplemented => "Unsupported",
            _ => "Unavailable"
        };
        var detail = statusCode switch
        {
            HttpStatusCode.Unauthorized => "The Honua server rejected the request because admin authentication is missing.",
            HttpStatusCode.Forbidden => "The current principal lacks permission to read catalog discovery endpoints.",
            HttpStatusCode.NotFound => "The Honua server does not expose the catalog discovery-endpoints registry (honua-server#1279).",
            _ => $"The Honua server returned HTTP {(int)statusCode} ({statusCode})."
        };
        return new HonuaAdminEndpointIssue(state, contract, detail, (int)statusCode);
    }
}

public sealed record HonuaCatalogFeeder
{
    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    [JsonPropertyName("label")]
    public required string Label { get; init; }
}

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

    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; }

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

public static class HonuaCatalogFieldStates
{
    public const string System = "system";
    public const string Calculated = "calculated";
    public const string Input = "input";
}

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
