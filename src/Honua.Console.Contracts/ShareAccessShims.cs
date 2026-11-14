using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Honua.Console.Contracts;

public static class HonuaConsoleShareAccessTiers
{
    public const string Private = "private";
    public const string Organization = "organization";
    public const string PublicLink = "public-link";
    public const string PublicIndexed = "public-indexed";

    public static bool IsPublic(string? tier) =>
        string.Equals(tier, PublicLink, StringComparison.Ordinal)
        || string.Equals(tier, PublicIndexed, StringComparison.Ordinal);
}

public static class HonuaConsoleEmbedAudiences
{
    public const string Map = "map";
    public const string Content = "content";
}

public sealed record HonuaConsoleShareClientOptions(Uri BaseUri, string? ApiKey = null);

public interface IHonuaConsoleShareClient
{
    Uri BaseUri { get; }

    Task<HonuaAdminEndpointResult<HonuaConsoleShareProjection>> GetShareAsync(
        string itemId,
        CancellationToken cancellationToken = default);

    Task<HonuaAdminEndpointResult<HonuaConsoleShareProjection>> UpdateAccessTierAsync(
        string itemId,
        HonuaUpdateShareAccessRequest request,
        CancellationToken cancellationToken = default);

    Task<HonuaAdminEndpointResult<HonuaConsoleShareDependencyClosure>> PreviewDependenciesAsync(
        string itemId,
        string targetTier,
        CancellationToken cancellationToken = default);

    // Token values are never returned after minting.
    Task<HonuaAdminEndpointResult<HonuaConsolePublicLinkList>> ListPublicLinksAsync(
        string itemId,
        CancellationToken cancellationToken = default);

    // The opaque token value is returned exactly once.
    Task<HonuaAdminEndpointResult<HonuaConsolePublicLinkToken>> MintPublicLinkAsync(
        string itemId,
        HonuaMintPublicLinkRequest request,
        CancellationToken cancellationToken = default);

    Task<HonuaAdminEndpointResult<HonuaConsoleShareCommandAck>> RevokePublicLinkAsync(
        string itemId,
        string tokenId,
        CancellationToken cancellationToken = default);

    Task<HonuaAdminEndpointResult<HonuaConsoleShareProjection>> SetEmbedAsync(
        string itemId,
        HonuaSetEmbedRequest request,
        CancellationToken cancellationToken = default);

    // The opaque token value is returned exactly once.
    Task<HonuaAdminEndpointResult<HonuaConsoleEmbedToken>> MintEmbedTokenAsync(
        string itemId,
        HonuaMintEmbedTokenRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class HonuaConsoleShareHttpClient : IHonuaConsoleShareClient, IDisposable
{
    private const string Base = "/api/v1/console/content";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly AdminBodyFormat EnvelopeFormat = new(
        JsonOptions,
        AdminBodyRead.Streamed,
        "The Honua server Console share response did not match the expected contract");

    private readonly HttpClient _httpClient;
    private readonly AdminEndpointTransport _transport;

    public HonuaConsoleShareHttpClient(HttpClient httpClient, HonuaConsoleShareClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);

        BaseUri = options.BaseUri;
        _httpClient = httpClient;
        _httpClient.BaseAddress ??= BaseUri;
        _transport = new AdminEndpointTransport(
            httpClient,
            options.ApiKey,
            AdminTransportFailure.Unreachable("The Honua server Console share endpoint"));
    }

    public Uri BaseUri { get; }

    public Task<HonuaAdminEndpointResult<HonuaConsoleShareProjection>> GetShareAsync(
        string itemId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);

        return SendAsync<HonuaConsoleShareProjection>(
            () => new HttpRequestMessage(HttpMethod.Get, ShareRoot(itemId)),
            "GET /api/v1/console/content/{id}/share",
            cancellationToken);
    }

    public Task<HonuaAdminEndpointResult<HonuaConsoleShareProjection>> UpdateAccessTierAsync(
        string itemId,
        HonuaUpdateShareAccessRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);
        ArgumentNullException.ThrowIfNull(request);

        return SendAsync<HonuaConsoleShareProjection>(
            () => JsonRequest(HttpMethod.Put, $"{ShareRoot(itemId)}/access", request),
            "PUT /api/v1/console/content/{id}/share/access",
            cancellationToken);
    }

    public Task<HonuaAdminEndpointResult<HonuaConsoleShareDependencyClosure>> PreviewDependenciesAsync(
        string itemId,
        string targetTier,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetTier);

        return SendAsync<HonuaConsoleShareDependencyClosure>(
            () => new HttpRequestMessage(HttpMethod.Get, $"{ShareRoot(itemId)}/dependencies?targetTier={Uri.EscapeDataString(targetTier)}"),
            "GET /api/v1/console/content/{id}/share/dependencies",
            cancellationToken);
    }

    // Token values are never returned after minting.
    public Task<HonuaAdminEndpointResult<HonuaConsolePublicLinkList>> ListPublicLinksAsync(
        string itemId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);

        return SendAsync<HonuaConsolePublicLinkList>(
            () => new HttpRequestMessage(HttpMethod.Get, $"{ShareRoot(itemId)}/link"),
            "GET /api/v1/console/content/{id}/share/link",
            cancellationToken);
    }

    // The opaque token value is returned exactly once.
    public Task<HonuaAdminEndpointResult<HonuaConsolePublicLinkToken>> MintPublicLinkAsync(
        string itemId,
        HonuaMintPublicLinkRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);
        ArgumentNullException.ThrowIfNull(request);

        return SendAsync<HonuaConsolePublicLinkToken>(
            () => JsonRequest(HttpMethod.Post, $"{ShareRoot(itemId)}/link", request),
            "POST /api/v1/console/content/{id}/share/link",
            cancellationToken);
    }

    public Task<HonuaAdminEndpointResult<HonuaConsoleShareCommandAck>> RevokePublicLinkAsync(
        string itemId,
        string tokenId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenId);

        return SendAsync<HonuaConsoleShareCommandAck>(
            () => new HttpRequestMessage(HttpMethod.Delete, $"{ShareRoot(itemId)}/link/{Uri.EscapeDataString(tokenId)}"),
            "DELETE /api/v1/console/content/{id}/share/link/{tokenId}",
            cancellationToken);
    }

    public Task<HonuaAdminEndpointResult<HonuaConsoleShareProjection>> SetEmbedAsync(
        string itemId,
        HonuaSetEmbedRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);
        ArgumentNullException.ThrowIfNull(request);

        return SendAsync<HonuaConsoleShareProjection>(
            () => JsonRequest(HttpMethod.Put, $"{ShareRoot(itemId)}/embed", request),
            "PUT /api/v1/console/content/{id}/share/embed",
            cancellationToken);
    }

    // The opaque token value is returned exactly once.
    public Task<HonuaAdminEndpointResult<HonuaConsoleEmbedToken>> MintEmbedTokenAsync(
        string itemId,
        HonuaMintEmbedTokenRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);
        ArgumentNullException.ThrowIfNull(request);

        return SendAsync<HonuaConsoleEmbedToken>(
            () => JsonRequest(HttpMethod.Post, $"{ShareRoot(itemId)}/embed", request),
            "POST /api/v1/console/content/{id}/share/embed",
            cancellationToken);
    }

    public void Dispose() => _httpClient.Dispose();

    private static string ShareRoot(string itemId) => $"{Base}/{Uri.EscapeDataString(itemId)}/share";

    private static HttpRequestMessage JsonRequest<TBody>(HttpMethod method, string path, TBody body) =>
        new(method, path) { Content = JsonContent.Create(body, options: JsonOptions) };

    private Task<HonuaAdminEndpointResult<T>> SendAsync<T>(
        Func<HttpRequestMessage> createRequest,
        string contract,
        CancellationToken cancellationToken) =>
        _transport.SendAsync(
            new AdminEndpointCall<T>(createRequest, contract, new(CreateIssueAsync, ShareEnvelope<T>()))
            {
                Completion = HttpCompletionOption.ResponseHeadersRead
            },
            cancellationToken);

    // A command that returns no projection (e.g. public-link revoke) answers success with data:null and a
    // message; that is a successful acknowledgement, not a missing projection.
    private static AdminResponseReader<T> ShareEnvelope<T>() =>
        AdminResponseBodies.Json<HonuaAdminApiResponse<T>, T>(EnvelopeFormat, (envelope, context) => envelope switch
        {
            { Success: true, Data: { } data } => HonuaAdminEndpointResult<T>.FromData(data),
            { Success: true } when typeof(T) == typeof(HonuaConsoleShareCommandAck) =>
                HonuaAdminEndpointResult<T>.FromData((T)(object)new HonuaConsoleShareCommandAck { Message = envelope.Message }),
            _ => context.Unavailable<T>(envelope?.Message ?? "The Honua server Console share response did not include data.")
        });

    private static async Task<HonuaAdminEndpointIssue> CreateIssueAsync(
        string contract,
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var statusCode = response.StatusCode;
        var state = AdminEndpointIssueFactory.MapState(statusCode);

        // A 409 on an access/embed change carries the blocking-dependency message.
        var serverMessage = await TryReadMessageAsync(response, cancellationToken).ConfigureAwait(false);

        var detail = serverMessage ?? statusCode switch
        {
            HttpStatusCode.Unauthorized => "The Honua server rejected the request because admin authentication is missing.",
            HttpStatusCode.Forbidden => "The Honua server rejected the request because the current principal lacks Console share permission.",
            HttpStatusCode.NotFound => "The Honua server has no Console item or share token with that ID.",
            HttpStatusCode.Conflict => "The Console share change was blocked by the dependency closure or share state.",
            HttpStatusCode.BadRequest => "The Honua server rejected the Console share request.",
            HttpStatusCode.MethodNotAllowed => "The Honua server exposes the Console share route but not the required verb.",
            HttpStatusCode.NotImplemented => "The Honua server reports the Console share capability is not implemented.",
            _ => string.Format(
                CultureInfo.InvariantCulture,
                "The Honua server returned HTTP {0} ({1}) for the Console share request.",
                (int)statusCode,
                statusCode)
        };

        return new HonuaAdminEndpointIssue(state, contract, detail, (int)statusCode);
    }

    private static async Task<string?> TryReadMessageAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentType?.MediaType is not "application/json")
        {
            return null;
        }

        try
        {
            var envelope = await response.Content
                .ReadFromJsonAsync<HonuaAdminApiResponse<HonuaConsoleShareDependencyClosure>>(JsonOptions, cancellationToken)
                .ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(envelope?.Message) ? null : envelope!.Message;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }
}

public sealed record HonuaUpdateShareAccessRequest
{
    [JsonPropertyName("accessTier")]
    public required string AccessTier { get; init; }

    [JsonPropertyName("allowDependencyConflicts")]
    public bool AllowDependencyConflicts { get; init; }
}

public sealed record HonuaMintPublicLinkRequest
{
    [JsonPropertyName("expiresAt")]
    public DateTimeOffset? ExpiresAt { get; init; }
}

public sealed record HonuaSetEmbedRequest
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; }

    [JsonPropertyName("audience")]
    public string? Audience { get; init; }

    [JsonPropertyName("allowDependencyConflicts")]
    public bool AllowDependencyConflicts { get; init; }
}

public sealed record HonuaMintEmbedTokenRequest
{
    [JsonPropertyName("audience")]
    public required string Audience { get; init; }

    [JsonPropertyName("ttlSeconds")]
    public int? TtlSeconds { get; init; }
}

public sealed record HonuaConsolePublicLinkToken
{
    [JsonPropertyName("tokenId")]
    public string TokenId { get; init; } = string.Empty;

    [JsonPropertyName("token")]
    public string? Token { get; init; }

    [JsonPropertyName("itemId")]
    public string ItemId { get; init; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; init; }

    [JsonPropertyName("expiresAt")]
    public DateTimeOffset? ExpiresAt { get; init; }

    [JsonPropertyName("createdById")]
    public string? CreatedById { get; init; }

    [JsonPropertyName("isExpired")]
    public bool IsExpired { get; init; }
}

public sealed record HonuaConsoleEmbedToken
{
    [JsonPropertyName("tokenId")]
    public string TokenId { get; init; } = string.Empty;

    [JsonPropertyName("token")]
    public string? Token { get; init; }

    [JsonPropertyName("itemId")]
    public string ItemId { get; init; } = string.Empty;

    [JsonPropertyName("audience")]
    public string Audience { get; init; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; init; }

    [JsonPropertyName("expiresAt")]
    public DateTimeOffset ExpiresAt { get; init; }

    [JsonPropertyName("createdById")]
    public string? CreatedById { get; init; }

    [JsonPropertyName("isExpired")]
    public bool IsExpired { get; init; }
}

public sealed record HonuaConsolePublicLinkList
{
    [JsonPropertyName("itemId")]
    public string ItemId { get; init; } = string.Empty;

    [JsonPropertyName("tokens")]
    public HonuaConsolePublicLinkToken[] Tokens { get; init; } = [];
}

public sealed record HonuaConsoleShareDependencyConflict
{
    [JsonPropertyName("itemId")]
    public string ItemId { get; init; } = string.Empty;

    [JsonPropertyName("itemType")]
    public string? ItemType { get; init; }

    [JsonPropertyName("itemName")]
    public string? ItemName { get; init; }

    [JsonPropertyName("blockingReason")]
    public string BlockingReason { get; init; } = string.Empty;
}

public sealed record HonuaConsoleShareDependencyClosure
{
    [JsonPropertyName("itemId")]
    public string ItemId { get; init; } = string.Empty;

    [JsonPropertyName("targetTier")]
    public string TargetTier { get; init; } = string.Empty;

    [JsonPropertyName("isCompatible")]
    public bool IsCompatible { get; init; }

    [JsonPropertyName("conflicts")]
    public HonuaConsoleShareDependencyConflict[] Conflicts { get; init; } = [];
}

public sealed record HonuaConsoleShareProjection
{
    [JsonPropertyName("itemId")]
    public string ItemId { get; init; } = string.Empty;

    [JsonPropertyName("itemName")]
    public string ItemName { get; init; } = string.Empty;

    [JsonPropertyName("itemTitle")]
    public string? ItemTitle { get; init; }

    [JsonPropertyName("itemType")]
    public string ItemType { get; init; } = string.Empty;

    [JsonPropertyName("ownerId")]
    public string? OwnerId { get; init; }

    [JsonPropertyName("accessTier")]
    public string AccessTier { get; init; } = HonuaConsoleShareAccessTiers.Private;

    [JsonPropertyName("publicLinkEnabled")]
    public bool PublicLinkEnabled { get; init; }

    [JsonPropertyName("publicLinkTokens")]
    public HonuaConsolePublicLinkToken[] PublicLinkTokens { get; init; } = [];

    [JsonPropertyName("embedEnabled")]
    public bool EmbedEnabled { get; init; }

    [JsonPropertyName("embedAudience")]
    public string? EmbedAudience { get; init; }

    [JsonPropertyName("openDataEligible")]
    public bool OpenDataEligible { get; init; }

    [JsonPropertyName("callerPermissions")]
    public string[] CallerPermissions { get; init; } = [];

    [JsonPropertyName("anonymousEligible")]
    public bool AnonymousEligible { get; init; }

    [JsonPropertyName("endpoints")]
    public Dictionary<string, string> Endpoints { get; init; } = [];

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset? UpdatedAt { get; init; }

    [JsonPropertyName("updatedById")]
    public string? UpdatedById { get; init; }
}

public sealed record HonuaConsoleShareCommandAck
{
    [JsonPropertyName("message")]
    public string? Message { get; init; }
}
