using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Honua.Console.Contracts;

public sealed record HonuaConsoleContentClientOptions(Uri BaseUri, string? ApiKey = null);

public interface IHonuaConsoleContentClient
{
    Uri BaseUri { get; }

    Task<HonuaAdminEndpointResult<HonuaConsoleContentListResponse>> ListAsync(
        HonuaConsoleContentListQuery query,
        CancellationToken cancellationToken = default);

    Task<HonuaAdminEndpointResult<HonuaConsoleContentListResponse>> SearchAsync(
        string term,
        int limit = 25,
        string? cursor = null,
        CancellationToken cancellationToken = default);

    Task<HonuaAdminEndpointResult<HonuaConsoleContentItem>> GetAsync(
        string id,
        CancellationToken cancellationToken = default);

    Task<HonuaAdminEndpointResult<HonuaConsoleActionCheckResponse>> CheckActionsAsync(
        HonuaConsoleActionCheckRequest request,
        CancellationToken cancellationToken = default);

    // The product authors content through Studio and Operate; live-server tests seed an item through this method.
    Task<HonuaAdminEndpointResult<HonuaConsoleContentItem>> CreateAsync(
        HonuaCreateConsoleContentItemRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class HonuaConsoleContentHttpClient : IHonuaConsoleContentClient, IDisposable
{
    private const string ContentBase = "/api/v1/console/content";
    private const string ActionsBase = "/api/v1/console/actions";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly AdminBodyFormat EnvelopeFormat = new(
        JsonOptions,
        AdminBodyRead.Streamed,
        "The Honua server Console content response did not match the expected contract");

    private readonly HttpClient _httpClient;
    private readonly AdminEndpointTransport _transport;

    public HonuaConsoleContentHttpClient(HttpClient httpClient, HonuaConsoleContentClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);

        BaseUri = options.BaseUri;
        _httpClient = httpClient;
        _httpClient.BaseAddress ??= BaseUri;
        _transport = new AdminEndpointTransport(
            httpClient,
            options.ApiKey,
            AdminTransportFailure.Unreachable("The Honua server Console content endpoint"));
    }

    public Uri BaseUri { get; }

    public Task<HonuaAdminEndpointResult<HonuaConsoleContentListResponse>> ListAsync(
        HonuaConsoleContentListQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var path = $"{ContentBase}/{BuildQueryString(query.ToParameters())}";
        return GetAsync<HonuaConsoleContentListResponse>(
            path,
            "GET /api/v1/console/content",
            cancellationToken);
    }

    public Task<HonuaAdminEndpointResult<HonuaConsoleContentListResponse>> SearchAsync(
        string term,
        int limit = 25,
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(term))
        {
            parameters["q"] = term;
        }

        parameters["limit"] = limit.ToString(CultureInfo.InvariantCulture);
        if (!string.IsNullOrWhiteSpace(cursor))
        {
            parameters["cursor"] = cursor;
        }

        return GetAsync<HonuaConsoleContentListResponse>(
            $"{ContentBase}/search{BuildQueryString(parameters)}",
            "GET /api/v1/console/content/search",
            cancellationToken);
    }

    public Task<HonuaAdminEndpointResult<HonuaConsoleContentItem>> GetAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        return GetAsync<HonuaConsoleContentItem>(
            $"{ContentBase}/{Uri.EscapeDataString(id)}",
            "GET /api/v1/console/content/{id}",
            cancellationToken);
    }

    public Task<HonuaAdminEndpointResult<HonuaConsoleActionCheckResponse>> CheckActionsAsync(
        HonuaConsoleActionCheckRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return SendAsync<HonuaConsoleActionCheckResponse>(
            () => JsonPost($"{ActionsBase}/check", request),
            "POST /api/v1/console/actions/check",
            cancellationToken);
    }

    public Task<HonuaAdminEndpointResult<HonuaConsoleContentItem>> CreateAsync(
        HonuaCreateConsoleContentItemRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return SendAsync<HonuaConsoleContentItem>(
            () => JsonPost($"{ContentBase}/", request),
            "POST /api/v1/console/content",
            cancellationToken);
    }

    public void Dispose() => _httpClient.Dispose();

    private Task<HonuaAdminEndpointResult<T>> GetAsync<T>(
        string path,
        string contract,
        CancellationToken cancellationToken) =>
        SendAsync<T>(() => new HttpRequestMessage(HttpMethod.Get, path), contract, cancellationToken);

    private static HttpRequestMessage JsonPost<TBody>(string path, TBody body) =>
        new(HttpMethod.Post, path) { Content = JsonContent.Create(body, options: JsonOptions) };

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
                    AdminResponseBodies.Envelope<T>(EnvelopeFormat, "The Honua server Console content response did not include data.")))
            {
                Completion = HttpCompletionOption.ResponseHeadersRead
            },
            cancellationToken);

    private static string BuildQueryString(IReadOnlyDictionary<string, string> parameters)
    {
        if (parameters.Count == 0)
        {
            return string.Empty;
        }

        var pairs = parameters
            .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .Select(static pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}");
        return $"?{string.Join('&', pairs)}";
    }
}

public sealed record HonuaConsoleContentListQuery
{
    public string? ItemType { get; init; }

    public string? Visibility { get; init; }

    public string? Owner { get; init; }

    public string? Namespace { get; init; }

    public string? SearchTerm { get; init; }

    public string? Cursor { get; init; }

    public int Limit { get; init; } = 25;

    public IReadOnlyDictionary<string, string> ToParameters()
    {
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
        AddIfPresent(parameters, "itemType", ItemType);
        AddIfPresent(parameters, "visibility", Visibility);
        AddIfPresent(parameters, "owner", Owner);
        AddIfPresent(parameters, "namespace", Namespace);
        AddIfPresent(parameters, "q", SearchTerm);
        AddIfPresent(parameters, "cursor", Cursor);
        parameters["limit"] = Limit.ToString(CultureInfo.InvariantCulture);
        return parameters;
    }

    private static void AddIfPresent(IDictionary<string, string> parameters, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            parameters[key] = value;
        }
    }
}

public static class HonuaConsoleContentItemTypes
{
    public const string Service = "service";
    public const string Layer = "layer";
    public const string SavedMap = "saved-map";
    public const string Dashboard = "dashboard";
    public const string Report = "report";
    public const string GeneratedApp = "generated-app";
    public const string OpenData = "open-data";
}

public static class HonuaConsoleVisibilities
{
    public const string Personal = "personal";
    public const string Team = "team";
    public const string Organization = "organization";
    public const string Public = "public";
}

public static class HonuaConsoleContentActions
{
    public const string View = "view";
    public const string Edit = "edit";
    public const string Publish = "publish";
    public const string Share = "share";
    public const string Embed = "embed";
    public const string Operate = "operate";
    public const string Administer = "administer";
}

public sealed record HonuaConsoleProvenanceRef
{
    [JsonPropertyName("kind")]
    public string Kind { get; init; } = string.Empty;

    [JsonPropertyName("itemId")]
    public string ItemId { get; init; } = string.Empty;

    [JsonPropertyName("rel")]
    public string Rel { get; init; } = string.Empty;

    [JsonPropertyName("namespace")]
    public string? Namespace { get; init; }
}

public sealed record HonuaConsoleContentItem
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("namespace")]
    public string? Namespace { get; init; }

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("itemType")]
    public string ItemType { get; init; } = string.Empty;

    [JsonPropertyName("tags")]
    public string[] Tags { get; init; } = [];

    [JsonPropertyName("labels")]
    public Dictionary<string, string> Labels { get; init; } = [];

    [JsonPropertyName("lifecycle")]
    public string? Lifecycle { get; init; }

    [JsonPropertyName("operationalState")]
    public string? OperationalState { get; init; }

    [JsonPropertyName("visibility")]
    public string Visibility { get; init; } = HonuaConsoleVisibilities.Personal;

    [JsonPropertyName("ownerId")]
    public string? OwnerId { get; init; }

    [JsonPropertyName("teamScopeId")]
    public string? TeamScopeId { get; init; }

    [JsonPropertyName("createdById")]
    public string? CreatedById { get; init; }

    [JsonPropertyName("updatedById")]
    public string? UpdatedById { get; init; }

    [JsonPropertyName("createdAt")]
    public DateTimeOffset? CreatedAt { get; init; }

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset? UpdatedAt { get; init; }

    [JsonPropertyName("generation")]
    public long? Generation { get; init; }

    [JsonPropertyName("actions")]
    public string[] Actions { get; init; } = [];

    [JsonPropertyName("provenance")]
    public HonuaConsoleProvenanceRef[] Provenance { get; init; } = [];

    [JsonPropertyName("typeMetadata")]
    public JsonElement? TypeMetadata { get; init; }
}

public sealed record HonuaCreateConsoleContentItemRequest
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("itemType")]
    public string ItemType { get; init; } = string.Empty;

    [JsonPropertyName("namespace")]
    public string? Namespace { get; init; }

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("tags")]
    public string[]? Tags { get; init; }

    [JsonPropertyName("visibility")]
    public string? Visibility { get; init; }

    [JsonPropertyName("ownerId")]
    public string? OwnerId { get; init; }

    [JsonPropertyName("teamScopeId")]
    public string? TeamScopeId { get; init; }

    [JsonPropertyName("provenance")]
    public HonuaConsoleProvenanceRef[]? Provenance { get; init; }
}

public sealed record HonuaConsoleContentListResponse
{
    [JsonPropertyName("items")]
    public HonuaConsoleContentItem[] Items { get; init; } = [];

    [JsonPropertyName("total")]
    public int Total { get; init; }

    [JsonPropertyName("nextCursor")]
    public string? NextCursor { get; init; }
}

public sealed record HonuaConsoleActionCheckTarget
{
    [JsonPropertyName("itemId")]
    public string? ItemId { get; init; }

    [JsonPropertyName("routeKey")]
    public string? RouteKey { get; init; }
}

public sealed record HonuaConsoleActionCheckRequest
{
    [JsonPropertyName("targets")]
    public HonuaConsoleActionCheckTarget[] Targets { get; init; } = [];

    [JsonPropertyName("actions")]
    public string[]? Actions { get; init; }
}

public sealed record HonuaConsoleActionCheckResult
{
    [JsonPropertyName("itemId")]
    public string? ItemId { get; init; }

    [JsonPropertyName("routeKey")]
    public string? RouteKey { get; init; }

    [JsonPropertyName("allowed")]
    public string[] Allowed { get; init; } = [];

    [JsonPropertyName("denied")]
    public string[] Denied { get; init; } = [];

    [JsonPropertyName("notFound")]
    public bool NotFound { get; init; }
}

public sealed record HonuaConsoleActionCheckResponse
{
    [JsonPropertyName("results")]
    public HonuaConsoleActionCheckResult[] Results { get; init; } = [];
}
