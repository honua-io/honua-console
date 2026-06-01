using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Honua.Console.Contracts;

// SHIM(honua-server#1278, slice 1): honua-server owns the durable Studio map collaboration contract — the
// feature-pinned comment threads (list/create/reply/resolve) and the collaboration activity feed for a map
// draft/package. The real-time presence/cursor/markup transport is a deferred follow-up slice
// (honua-server#1290): the Console collaboration surface (honua-console#124) keeps rendering its
// missing-binding state for those live slots, and this shim binds ONLY the durable comments + activity reads.
//
// The reads/writes are admin reads/mutations wrapped in the shared {success,data,message,timestamp} envelope
// (Honua.Infrastructure.Models.ApiResponse<T>), so this client reuses the shared HonuaAdminApiResponse<T>
// envelope and the HonuaAdminEndpointResult<T>/HonuaAdminEndpointIssue records (defined in
// OperateAdminShims.cs). The wire records below mirror the honua-server projection
// (Honua.Server.Features.Console.Collaboration.Models.*); the JSON shapes are deliberately aligned with the
// Console StudioMapCommentPin / StudioMapActivityEntry view models so the comment drawer + activity sidebar
// bind directly. Swap these for SDK types when honua-sdk-dotnet ships a consumable projection.

public sealed record HonuaStudioMapCollaborationClientOptions(Uri BaseUri, string? ApiKey = null);

/// <summary>
/// Typed client for the honua-server durable Studio map collaboration API (honua-server#1278, slice 1).
/// Reads/writes target the map-scoped admin routes under <c>/api/v{version}/console/maps/{mapId}/collab</c>.
/// </summary>
public interface IHonuaStudioMapCollaborationClient
{
    Uri BaseUri { get; }

    /// <summary>Lists the feature-pinned comment threads for a map draft/package (newest activity first).</summary>
    Task<HonuaAdminEndpointResult<HonuaStudioMapCommentThreadList>> ListThreadsAsync(
        string mapId,
        CancellationToken cancellationToken = default);

    /// <summary>Lists the durable collaboration activity events for a map draft/package (newest first).</summary>
    Task<HonuaAdminEndpointResult<HonuaStudioMapActivityList>> ListActivityAsync(
        string mapId,
        int? limit = null,
        CancellationToken cancellationToken = default);

    /// <summary>Opens a feature-pinned comment thread with its first message and anchor metadata.</summary>
    Task<HonuaAdminEndpointResult<HonuaStudioMapCommentThread>> CreateThreadAsync(
        string mapId,
        HonuaCreateStudioMapCommentThreadRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Appends a reply to an existing comment thread.</summary>
    Task<HonuaAdminEndpointResult<HonuaStudioMapCommentThread>> AddReplyAsync(
        string mapId,
        string threadId,
        HonuaCreateStudioMapCommentReplyRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Resolves or reopens a comment thread.</summary>
    Task<HonuaAdminEndpointResult<HonuaStudioMapCommentThread>> SetResolvedAsync(
        string mapId,
        string threadId,
        HonuaResolveStudioMapCommentThreadRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class HonuaStudioMapCollaborationHttpClient : IHonuaStudioMapCollaborationClient, IDisposable
{
    private const string Base = "/api/v1/console/maps";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly string? _apiKey;

    public HonuaStudioMapCollaborationHttpClient(HttpClient httpClient, HonuaStudioMapCollaborationClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);

        BaseUri = options.BaseUri;
        _apiKey = options.ApiKey;
        _httpClient = httpClient;
        _httpClient.BaseAddress ??= BaseUri;
    }

    public Uri BaseUri { get; }

    public Task<HonuaAdminEndpointResult<HonuaStudioMapCommentThreadList>> ListThreadsAsync(
        string mapId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mapId);

        return SendGetAsync<HonuaStudioMapCommentThreadList>(
            $"{Base}/{Uri.EscapeDataString(mapId)}/collab/comments",
            "GET /api/v1/console/maps/{mapId}/collab/comments",
            cancellationToken);
    }

    public Task<HonuaAdminEndpointResult<HonuaStudioMapActivityList>> ListActivityAsync(
        string mapId,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mapId);

        var path = $"{Base}/{Uri.EscapeDataString(mapId)}/collab/activity";
        if (limit is { } value)
        {
            path += $"?limit={value.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        }

        return SendGetAsync<HonuaStudioMapActivityList>(
            path,
            "GET /api/v1/console/maps/{mapId}/collab/activity",
            cancellationToken);
    }

    public Task<HonuaAdminEndpointResult<HonuaStudioMapCommentThread>> CreateThreadAsync(
        string mapId,
        HonuaCreateStudioMapCommentThreadRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mapId);
        ArgumentNullException.ThrowIfNull(request);

        return SendPostAsync<HonuaCreateStudioMapCommentThreadRequest, HonuaStudioMapCommentThread>(
            $"{Base}/{Uri.EscapeDataString(mapId)}/collab/comments",
            request,
            "POST /api/v1/console/maps/{mapId}/collab/comments",
            cancellationToken);
    }

    public Task<HonuaAdminEndpointResult<HonuaStudioMapCommentThread>> AddReplyAsync(
        string mapId,
        string threadId,
        HonuaCreateStudioMapCommentReplyRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mapId);
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        ArgumentNullException.ThrowIfNull(request);

        return SendPostAsync<HonuaCreateStudioMapCommentReplyRequest, HonuaStudioMapCommentThread>(
            $"{Base}/{Uri.EscapeDataString(mapId)}/collab/comments/{Uri.EscapeDataString(threadId)}/replies",
            request,
            "POST /api/v1/console/maps/{mapId}/collab/comments/{threadId}/replies",
            cancellationToken);
    }

    public Task<HonuaAdminEndpointResult<HonuaStudioMapCommentThread>> SetResolvedAsync(
        string mapId,
        string threadId,
        HonuaResolveStudioMapCommentThreadRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mapId);
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        ArgumentNullException.ThrowIfNull(request);

        return SendPostAsync<HonuaResolveStudioMapCommentThreadRequest, HonuaStudioMapCommentThread>(
            $"{Base}/{Uri.EscapeDataString(mapId)}/collab/comments/{Uri.EscapeDataString(threadId)}/resolve",
            request,
            "POST /api/v1/console/maps/{mapId}/collab/comments/{threadId}/resolve",
            cancellationToken);
    }

    public void Dispose() => _httpClient.Dispose();

    private async Task<HonuaAdminEndpointResult<T>> SendGetAsync<T>(
        string path,
        string contract,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        return await SendAsync<T>(request, contract, cancellationToken).ConfigureAwait(false);
    }

    private async Task<HonuaAdminEndpointResult<TResponse>> SendPostAsync<TRequest, TResponse>(
        string path,
        TRequest body,
        string contract,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body, options: JsonOptions)
        };
        return await SendAsync<TResponse>(request, contract, cancellationToken).ConfigureAwait(false);
    }

    private async Task<HonuaAdminEndpointResult<T>> SendAsync<T>(
        HttpRequestMessage request,
        string contract,
        CancellationToken cancellationToken)
    {
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
                    System.Net.HttpStatusCode.BadRequest => "Invalid request",
                    _ => "Unavailable"
                };
                var detail = response.StatusCode switch
                {
                    System.Net.HttpStatusCode.Unauthorized => "The Honua server rejected the request because admin authentication is missing.",
                    System.Net.HttpStatusCode.Forbidden => "The current principal lacks permission to read or post map collaboration comments.",
                    System.Net.HttpStatusCode.NotFound => "The Honua server does not expose the durable Studio map collaboration API (honua-server#1278), or the thread does not exist.",
                    System.Net.HttpStatusCode.BadRequest => "The Honua server rejected the map collaboration request as invalid.",
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
                    $"The Honua server map collaboration response did not match the expected contract: {ex.Message}",
                    (int)response.StatusCode));
            }

            if (envelope?.Success == true && envelope.Data is not null)
            {
                return HonuaAdminEndpointResult<T>.FromData(envelope.Data);
            }

            return HonuaAdminEndpointResult<T>.FromIssue(new HonuaAdminEndpointIssue(
                "Unavailable",
                contract,
                envelope?.Message ?? "The Honua server map collaboration response did not include data.",
                (int)response.StatusCode));
        }
    }

    private static HonuaAdminEndpointResult<T> Unavailable<T>(string contract, string detail) =>
        HonuaAdminEndpointResult<T>.FromIssue(new HonuaAdminEndpointIssue(
            "Unavailable",
            contract,
            $"The Honua server map collaboration endpoint could not be reached: {detail}"));
}

// --- Wire records mirroring the honua-server projection (#1278, slice 1). ---

/// <summary>One message in a feature-pinned comment thread (mirrors honua-server StudioMapCommentMessageDto).</summary>
public sealed record HonuaStudioMapCommentMessage
{
    [JsonPropertyName("messageId")]
    public required string MessageId { get; init; }

    [JsonPropertyName("authorName")]
    public required string AuthorName { get; init; }

    [JsonPropertyName("authorInitials")]
    public required string AuthorInitials { get; init; }

    [JsonPropertyName("authorColor")]
    public required string AuthorColor { get; init; }

    [JsonPropertyName("relativeTime")]
    public required string RelativeTime { get; init; }

    [JsonPropertyName("body")]
    public required string Body { get; init; }
}

/// <summary>One feature-pinned comment thread (mirrors honua-server StudioMapCommentThreadDto).</summary>
public sealed record HonuaStudioMapCommentThread
{
    [JsonPropertyName("threadId")]
    public required string ThreadId { get; init; }

    [JsonPropertyName("featureLabel")]
    public required string FeatureLabel { get; init; }

    [JsonPropertyName("layerRef")]
    public required string LayerRef { get; init; }

    [JsonPropertyName("commentCount")]
    public int CommentCount { get; init; }

    [JsonPropertyName("resolved")]
    public bool Resolved { get; init; }

    [JsonPropertyName("xFraction")]
    public double XFraction { get; init; }

    [JsonPropertyName("yFraction")]
    public double YFraction { get; init; }

    [JsonPropertyName("messages")]
    public IReadOnlyList<HonuaStudioMapCommentMessage> Messages { get; init; } = Array.Empty<HonuaStudioMapCommentMessage>();
}

/// <summary>List response wrapper for comment threads on a map (mirrors honua-server StudioMapCommentThreadListResponse).</summary>
public sealed record HonuaStudioMapCommentThreadList
{
    [JsonPropertyName("mapId")]
    public required string MapId { get; init; }

    [JsonPropertyName("threads")]
    public IReadOnlyList<HonuaStudioMapCommentThread> Threads { get; init; } = Array.Empty<HonuaStudioMapCommentThread>();
}

/// <summary>One collaboration activity-feed entry (mirrors honua-server StudioMapActivityEntryDto).</summary>
public sealed record HonuaStudioMapActivityEntry
{
    [JsonPropertyName("participantName")]
    public required string ParticipantName { get; init; }

    [JsonPropertyName("initials")]
    public required string Initials { get; init; }

    [JsonPropertyName("color")]
    public required string Color { get; init; }

    [JsonPropertyName("relativeTime")]
    public required string RelativeTime { get; init; }

    [JsonPropertyName("action")]
    public required string Action { get; init; }
}

/// <summary>List response wrapper for the activity feed on a map (mirrors honua-server StudioMapActivityListResponse).</summary>
public sealed record HonuaStudioMapActivityList
{
    [JsonPropertyName("mapId")]
    public required string MapId { get; init; }

    [JsonPropertyName("activity")]
    public IReadOnlyList<HonuaStudioMapActivityEntry> Activity { get; init; } = Array.Empty<HonuaStudioMapActivityEntry>();
}

/// <summary>Request body for opening a feature-pinned comment thread (mirrors honua-server CreateStudioMapCommentThreadRequest).</summary>
public sealed record HonuaCreateStudioMapCommentThreadRequest
{
    [JsonPropertyName("featureLabel")]
    public required string FeatureLabel { get; init; }

    [JsonPropertyName("layerRef")]
    public required string LayerRef { get; init; }

    [JsonPropertyName("xFraction")]
    public required double XFraction { get; init; }

    [JsonPropertyName("yFraction")]
    public required double YFraction { get; init; }

    [JsonPropertyName("body")]
    public required string Body { get; init; }
}

/// <summary>Request body for replying to a comment thread (mirrors honua-server CreateStudioMapCommentReplyRequest).</summary>
public sealed record HonuaCreateStudioMapCommentReplyRequest
{
    [JsonPropertyName("body")]
    public required string Body { get; init; }
}

/// <summary>Request body for resolving/reopening a comment thread (mirrors honua-server ResolveStudioMapCommentThreadRequest).</summary>
public sealed record HonuaResolveStudioMapCommentThreadRequest
{
    [JsonPropertyName("resolved")]
    public bool Resolved { get; init; } = true;
}
