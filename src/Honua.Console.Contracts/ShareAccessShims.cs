using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Honua.Console.Contracts;

// SHIM(honua-server#1215 / honua-sdk-dotnet#171): honua-server owns the Console Share access surface
// for content items — the share access tier, public-link token lifecycle, embed enablement/token
// lifecycle, dependency-closure preview, and the anonymous public-link/content/embed resolution
// endpoints. These live behind /api/v1/console/content/{id}/share (admin) and /api/v1/console/share
// (anonymous) in Honua.Server.Features.Console (ConsoleShareEndpoints / ConsoleSharePublicEndpoints,
// projecting Honua.Core.Features.Console.Domain.ConsoleShareProjection). honua-sdk-dotnet does not yet
// project these as a consumable stable package, and honua-console wires no SDK NuGet feed (see
// SDK_SHIM_POLICY.md and the form/operate/content-publication shims). Per the Console Patterns Charter
// section 11 and SDK_SHIM_POLICY, the Console Share surface binds through a thin HttpClient behind this
// single Honua.Console.Contracts boundary: the wire records below mirror the server projection and the
// client speaks the real console share lifecycle. Do not add a sibling-repo ProjectReference. Swap these
// for SDK types when honua-sdk-dotnet ships a consumable Console Share projection (honua-sdk-dotnet#171).
//
// The authenticated share routes are admin routes wrapped in the shared {success,data,message,timestamp}
// envelope (Honua.Infrastructure.Models.ApiResponse<T>), so this client reuses the shared
// HonuaAdminApiResponse<T> envelope and the HonuaAdminEndpointResult<T>/HonuaAdminEndpointIssue result
// records (defined in OperateAdminShims.cs). A 409 on an access/embed change carries the
// dependency-closure body, which this client surfaces as a typed Conflict issue so the Share panel can
// render the blocking dependencies rather than a generic failure.

/// <summary>Share access tier of a Console content item (mirrors honua-server ConsoleShareAccessTier).</summary>
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

/// <summary>Embed audience scope of a Console embed authorization (mirrors honua-server ConsoleEmbedAudience).</summary>
public static class HonuaConsoleEmbedAudiences
{
    public const string Map = "map";
    public const string Content = "content";
}

public sealed record HonuaConsoleShareClientOptions(Uri BaseUri, string? ApiKey = null);

/// <summary>
/// Typed client for the honua-server Console Share access surface (honua-server#1215). All authenticated
/// reads/mutations target <c>/api/v1/console/content/{id}/share</c>; the anonymous resolution helpers
/// target <c>/api/v1/console/share</c>.
/// </summary>
public interface IHonuaConsoleShareClient
{
    Uri BaseUri { get; }

    /// <summary>Reads the server-owned Share projection (access tier, public-link/embed state, permissions).</summary>
    Task<HonuaAdminEndpointResult<HonuaConsoleShareProjection>> GetShareAsync(
        string itemId,
        CancellationToken cancellationToken = default);

    /// <summary>Changes the item's Share access tier. A blocked public change surfaces a Conflict issue.</summary>
    Task<HonuaAdminEndpointResult<HonuaConsoleShareProjection>> UpdateAccessTierAsync(
        string itemId,
        HonuaUpdateShareAccessRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Previews whether the item's provenance closure is shareable by a target tier (no commit).</summary>
    Task<HonuaAdminEndpointResult<HonuaConsoleShareDependencyClosure>> PreviewDependenciesAsync(
        string itemId,
        string targetTier,
        CancellationToken cancellationToken = default);

    /// <summary>Lists public-link tokens for the item (token values are never returned after minting).</summary>
    Task<HonuaAdminEndpointResult<HonuaConsolePublicLinkList>> ListPublicLinksAsync(
        string itemId,
        CancellationToken cancellationToken = default);

    /// <summary>Mints a public-link token (the opaque value is returned exactly once).</summary>
    Task<HonuaAdminEndpointResult<HonuaConsolePublicLinkToken>> MintPublicLinkAsync(
        string itemId,
        HonuaMintPublicLinkRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Revokes a public-link token by id.</summary>
    Task<HonuaAdminEndpointResult<HonuaConsoleShareCommandAck>> RevokePublicLinkAsync(
        string itemId,
        string tokenId,
        CancellationToken cancellationToken = default);

    /// <summary>Enables or disables embedding with an audience scope.</summary>
    Task<HonuaAdminEndpointResult<HonuaConsoleShareProjection>> SetEmbedAsync(
        string itemId,
        HonuaSetEmbedRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Mints an embed token (the opaque value is returned exactly once).</summary>
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

    private readonly HttpClient _httpClient;
    private readonly string? _apiKey;

    public HonuaConsoleShareHttpClient(HttpClient httpClient, HonuaConsoleShareClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);

        BaseUri = options.BaseUri;
        _apiKey = options.ApiKey;
        _httpClient = httpClient;
        _httpClient.BaseAddress ??= BaseUri;
    }

    public Uri BaseUri { get; }

    public Task<HonuaAdminEndpointResult<HonuaConsoleShareProjection>> GetShareAsync(
        string itemId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);

        return SendAsync<HonuaConsoleShareProjection>(
            HttpMethod.Get,
            ShareRoot(itemId),
            content: null,
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
            HttpMethod.Put,
            $"{ShareRoot(itemId)}/access",
            JsonContent.Create(request, options: JsonOptions),
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
            HttpMethod.Get,
            $"{ShareRoot(itemId)}/dependencies?targetTier={Uri.EscapeDataString(targetTier)}",
            content: null,
            "GET /api/v1/console/content/{id}/share/dependencies",
            cancellationToken);
    }

    public Task<HonuaAdminEndpointResult<HonuaConsolePublicLinkList>> ListPublicLinksAsync(
        string itemId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);

        return SendAsync<HonuaConsolePublicLinkList>(
            HttpMethod.Get,
            $"{ShareRoot(itemId)}/link",
            content: null,
            "GET /api/v1/console/content/{id}/share/link",
            cancellationToken);
    }

    public Task<HonuaAdminEndpointResult<HonuaConsolePublicLinkToken>> MintPublicLinkAsync(
        string itemId,
        HonuaMintPublicLinkRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);
        ArgumentNullException.ThrowIfNull(request);

        return SendAsync<HonuaConsolePublicLinkToken>(
            HttpMethod.Post,
            $"{ShareRoot(itemId)}/link",
            JsonContent.Create(request, options: JsonOptions),
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
            HttpMethod.Delete,
            $"{ShareRoot(itemId)}/link/{Uri.EscapeDataString(tokenId)}",
            content: null,
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
            HttpMethod.Put,
            $"{ShareRoot(itemId)}/embed",
            JsonContent.Create(request, options: JsonOptions),
            "PUT /api/v1/console/content/{id}/share/embed",
            cancellationToken);
    }

    public Task<HonuaAdminEndpointResult<HonuaConsoleEmbedToken>> MintEmbedTokenAsync(
        string itemId,
        HonuaMintEmbedTokenRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);
        ArgumentNullException.ThrowIfNull(request);

        return SendAsync<HonuaConsoleEmbedToken>(
            HttpMethod.Post,
            $"{ShareRoot(itemId)}/embed",
            JsonContent.Create(request, options: JsonOptions),
            "POST /api/v1/console/content/{id}/share/embed",
            cancellationToken);
    }

    public void Dispose() => _httpClient.Dispose();

    private static string ShareRoot(string itemId) => $"{Base}/{Uri.EscapeDataString(itemId)}/share";

    private async Task<HonuaAdminEndpointResult<T>> SendAsync<T>(
        HttpMethod method,
        string path,
        HttpContent? content,
        string contract,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        if (content is not null)
        {
            request.Content = content;
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
            return Unavailable<T>(contract, ex.Message);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // A TaskCanceledException whose token is not the caller's is an HttpClient timeout, not caller
            // cancellation, so surface it as Unavailable. Caller-requested cancellation is left to propagate
            // (not caught) so it cancels the calling operation instead of being masked as a transport failure.
            return Unavailable<T>(contract, ex.Message);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                return HonuaAdminEndpointResult<T>.FromIssue(await CreateIssueAsync(contract, response, cancellationToken).ConfigureAwait(false));
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
                    $"The Honua server Console share response did not match the expected contract: {ex.Message}",
                    (int)response.StatusCode));
            }

            if (envelope?.Success == true)
            {
                if (envelope.Data is not null)
                {
                    return HonuaAdminEndpointResult<T>.FromData(envelope.Data);
                }

                // A successful command that returns no projection (e.g. public-link revoke) wraps a
                // message-only ack with data:null. Surface it as a successful ack rather than a false
                // Unavailable so callers (and the Share lifecycle round-trip) see the operation succeed.
                if (typeof(T) == typeof(HonuaConsoleShareCommandAck))
                {
                    var ack = (T)(object)new HonuaConsoleShareCommandAck { Message = envelope.Message };
                    return HonuaAdminEndpointResult<T>.FromData(ack);
                }
            }

            return HonuaAdminEndpointResult<T>.FromIssue(new HonuaAdminEndpointIssue(
                "Unavailable",
                contract,
                envelope?.Message ?? "The Honua server Console share response did not include data.",
                (int)response.StatusCode));
        }
    }

    private static HonuaAdminEndpointResult<T> Unavailable<T>(string contract, string detail) =>
        HonuaAdminEndpointResult<T>.FromIssue(new HonuaAdminEndpointIssue(
            "Unavailable",
            contract,
            $"The Honua server Console share endpoint could not be reached: {detail}"));

    private static async Task<HonuaAdminEndpointIssue> CreateIssueAsync(
        string contract,
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var statusCode = response.StatusCode;
        // Canonical status→state mapping shared across all admin shims (honua-console#279 PA-239).
        var state = AdminEndpointIssueFactory.MapState(statusCode);

        // A 409 on access/embed change carries the dependency-closure body. Surface the server-authored
        // message (the blocking-dependency detail) so the Share panel can explain why the change is blocked.
        var serverMessage = await TryReadMessageAsync(response, cancellationToken).ConfigureAwait(false);

        var detail = serverMessage ?? statusCode switch
        {
            HttpStatusCode.Unauthorized => "The Honua server rejected the request because admin authentication is missing.",
            HttpStatusCode.Forbidden => "The Honua server rejected the request because the current principal lacks Console share permission.",
            HttpStatusCode.NotFound => "The Honua server Console content item or share token was not found.",
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

// --- Wire records mirroring honua-server Honua.Core.Features.Console.Domain (#1215). ---

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

/// <summary>Acknowledgement body for a Console share command that returns no projection (e.g. token revoke).</summary>
public sealed record HonuaConsoleShareCommandAck
{
    [JsonPropertyName("message")]
    public string? Message { get; init; }
}
