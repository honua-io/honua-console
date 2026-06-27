using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Honua.Console.Contracts;

// SHIM(honua-server#1166 / honua-server#1167 / honua-sdk-dotnet): honua-server owns the temporal data
// history API (Honua.Server.Features.Temporal: capability discovery + as-of read, slice 1 of #1166) and
// the disconnected replica management API (Honua.Server.Features.Admin.ReplicaManagementEndpoints:
// replica list + detail in slice 1 of #1167, and conflict review + resolution in slice 2 of #1167).
// honua-sdk-dotnet does not yet project these as a consumable stable package, and honua-console wires no
// SDK NuGet feed (see SDK_SHIM_POLICY.md). Per the Console Patterns Charter section 11 and
// SDK_SHIM_POLICY, the temporal viewer binds through a thin HttpClient behind this single
// Honua.Console.Contracts boundary: the wire records below mirror the server responses, and the client
// speaks the real routes. Swap these for SDK types when honua-sdk-dotnet ships a consumable temporal
// projection and honua-console#7 wires the feed.
//
// The temporal capability + as-of endpoints return their DTO body directly (Results.Json of the
// response). The replica management endpoints (list/detail and conflict list/detail/resolve) wrap their
// payload in the shared ApiResponse<T> envelope ({success,data,message}); this client deserializes each
// accordingly and maps status semantics (400 validation, 404 not found, 409 conflict/already-resolved,
// 501 not-supported, 401/403 auth) to issues.
//
// MERGED SCOPE: #1166 slice 1 (capabilities + as-of), #1166 slices 2-5 (diff + feature timeline +
// governed rollback plan/execute, shipped as honua-server#1285), and #1167 slices 1+2 (replica
// list/detail + conflict review/resolution). The diff/timeline/rollback-plan endpoints return their DTO
// body directly (Results.Json of the response, like capabilities/as-of); rollback execute returns a job
// handle directly with HTTP 202. Each is bound here; an honest 404/forbidden/unsupported is mapped to an
// issue rather than fabricated.
public sealed record HonuaTemporalClientOptions(Uri BaseUri, string? ApiKey = null);

public interface IHonuaTemporalClient
{
    Uri BaseUri { get; }

    Task<HonuaAdminEndpointResult<HonuaTemporalCapabilityResponse>> GetCapabilityAsync(
        string serviceId,
        int layerId,
        CancellationToken cancellationToken = default);

    Task<HonuaAdminEndpointResult<HonuaTemporalAsOfResponse>> ReadAsOfAsync(
        string serviceId,
        int layerId,
        long? generation,
        string? timestamp,
        int? limit,
        CancellationToken cancellationToken = default);

    Task<HonuaAdminEndpointResult<HonuaTemporalDiffResponse>> GetDiffAsync(
        string serviceId,
        int layerId,
        string from,
        string? to,
        int? limit,
        CancellationToken cancellationToken = default);

    Task<HonuaAdminEndpointResult<HonuaTemporalTimelineResponse>> GetFeatureTimelineAsync(
        string serviceId,
        int layerId,
        long featureId,
        int? limit,
        CancellationToken cancellationToken = default);

    Task<HonuaAdminEndpointResult<HonuaTemporalRollbackPlanResponse>> PlanRollbackAsync(
        string serviceId,
        int layerId,
        HonuaTemporalRollbackPlanRequest request,
        CancellationToken cancellationToken = default);

    Task<HonuaAdminEndpointResult<HonuaTemporalRollbackJobResponse>> ExecuteRollbackAsync(
        string serviceId,
        int layerId,
        HonuaTemporalRollbackExecuteRequest request,
        CancellationToken cancellationToken = default);

    Task<HonuaAdminEndpointResult<HonuaReplicaManagementListResponse>> ListReplicasAsync(
        string serviceId,
        CancellationToken cancellationToken = default);

    Task<HonuaAdminEndpointResult<HonuaReplicaManagementDetail>> GetReplicaAsync(
        string serviceId,
        string replicaId,
        CancellationToken cancellationToken = default);

    Task<HonuaAdminEndpointResult<HonuaReplicaConflictListResponse>> ListReplicaConflictsAsync(
        string serviceId,
        string replicaId,
        string? status,
        CancellationToken cancellationToken = default);

    Task<HonuaAdminEndpointResult<HonuaReplicaConflictDetail>> GetReplicaConflictAsync(
        string serviceId,
        string replicaId,
        string conflictId,
        CancellationToken cancellationToken = default);

    Task<HonuaAdminEndpointResult<HonuaReplicaConflictResolutionResponse>> ResolveReplicaConflictAsync(
        string serviceId,
        string replicaId,
        string conflictId,
        HonuaReplicaConflictResolutionRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class HonuaTemporalHttpClient : IHonuaTemporalClient, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly string? _apiKey;

    public HonuaTemporalHttpClient(HttpClient httpClient, HonuaTemporalClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);

        BaseUri = options.BaseUri;
        _apiKey = options.ApiKey;
        _httpClient = httpClient;
        _httpClient.BaseAddress ??= BaseUri;
    }

    public Uri BaseUri { get; }

    public Task<HonuaAdminEndpointResult<HonuaTemporalCapabilityResponse>> GetCapabilityAsync(
        string serviceId,
        int layerId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceId);

        return GetDirectAsync<HonuaTemporalCapabilityResponse>(
            $"/api/v1/temporal/services/{Uri.EscapeDataString(serviceId)}/layers/{Layer(layerId)}/capabilities",
            "GET /api/v1/temporal/services/{serviceId}/layers/{layerId}/capabilities",
            cancellationToken);
    }

    public Task<HonuaAdminEndpointResult<HonuaTemporalAsOfResponse>> ReadAsOfAsync(
        string serviceId,
        int layerId,
        long? generation,
        string? timestamp,
        int? limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceId);

        var parameters = new List<string>();
        if (generation is { } gen)
        {
            parameters.Add($"generation={gen.ToString(CultureInfo.InvariantCulture)}");
        }

        if (!string.IsNullOrWhiteSpace(timestamp))
        {
            parameters.Add($"timestamp={Uri.EscapeDataString(timestamp)}");
        }

        if (limit is { } lim)
        {
            parameters.Add($"limit={lim.ToString(CultureInfo.InvariantCulture)}");
        }

        var queryString = parameters.Count == 0 ? string.Empty : "?" + string.Join("&", parameters);

        return GetDirectAsync<HonuaTemporalAsOfResponse>(
            $"/api/v1/temporal/services/{Uri.EscapeDataString(serviceId)}/layers/{Layer(layerId)}/as-of{queryString}",
            "GET /api/v1/temporal/services/{serviceId}/layers/{layerId}/as-of",
            cancellationToken);
    }

    public Task<HonuaAdminEndpointResult<HonuaTemporalDiffResponse>> GetDiffAsync(
        string serviceId,
        int layerId,
        string from,
        string? to,
        int? limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(from);

        var parameters = new List<string> { $"from={Uri.EscapeDataString(from)}" };
        if (!string.IsNullOrWhiteSpace(to))
        {
            parameters.Add($"to={Uri.EscapeDataString(to)}");
        }

        if (limit is { } lim)
        {
            parameters.Add($"limit={lim.ToString(CultureInfo.InvariantCulture)}");
        }

        return GetDirectAsync<HonuaTemporalDiffResponse>(
            $"/api/v1/temporal/services/{Uri.EscapeDataString(serviceId)}/layers/{Layer(layerId)}/diff?{string.Join("&", parameters)}",
            "GET /api/v1/temporal/services/{serviceId}/layers/{layerId}/diff",
            cancellationToken);
    }

    public Task<HonuaAdminEndpointResult<HonuaTemporalTimelineResponse>> GetFeatureTimelineAsync(
        string serviceId,
        int layerId,
        long featureId,
        int? limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceId);

        var queryString = limit is { } lim
            ? $"?limit={lim.ToString(CultureInfo.InvariantCulture)}"
            : string.Empty;

        return GetDirectAsync<HonuaTemporalTimelineResponse>(
            $"/api/v1/temporal/services/{Uri.EscapeDataString(serviceId)}/layers/{Layer(layerId)}/features/{featureId.ToString(CultureInfo.InvariantCulture)}/timeline{queryString}",
            "GET /api/v1/temporal/services/{serviceId}/layers/{layerId}/features/{featureId}/timeline",
            cancellationToken);
    }

    public Task<HonuaAdminEndpointResult<HonuaTemporalRollbackPlanResponse>> PlanRollbackAsync(
        string serviceId,
        int layerId,
        HonuaTemporalRollbackPlanRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceId);
        ArgumentNullException.ThrowIfNull(request);

        return PostDirectAsync<HonuaTemporalRollbackPlanRequest, HonuaTemporalRollbackPlanResponse>(
            $"/api/v1/temporal/services/{Uri.EscapeDataString(serviceId)}/layers/{Layer(layerId)}/rollback/plan",
            "POST /api/v1/temporal/services/{serviceId}/layers/{layerId}/rollback/plan",
            request,
            cancellationToken);
    }

    public Task<HonuaAdminEndpointResult<HonuaTemporalRollbackJobResponse>> ExecuteRollbackAsync(
        string serviceId,
        int layerId,
        HonuaTemporalRollbackExecuteRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceId);
        ArgumentNullException.ThrowIfNull(request);

        return PostDirectAsync<HonuaTemporalRollbackExecuteRequest, HonuaTemporalRollbackJobResponse>(
            $"/api/v1/temporal/services/{Uri.EscapeDataString(serviceId)}/layers/{Layer(layerId)}/rollback",
            "POST /api/v1/temporal/services/{serviceId}/layers/{layerId}/rollback",
            request,
            cancellationToken);
    }

    public Task<HonuaAdminEndpointResult<HonuaReplicaManagementListResponse>> ListReplicasAsync(
        string serviceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceId);

        return GetEnvelopeAsync<HonuaReplicaManagementListResponse>(
            $"/api/v1/admin/services/{Uri.EscapeDataString(serviceId)}/replicas/",
            "GET /api/v1/admin/services/{serviceId}/replicas",
            cancellationToken);
    }

    public Task<HonuaAdminEndpointResult<HonuaReplicaManagementDetail>> GetReplicaAsync(
        string serviceId,
        string replicaId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(replicaId);

        return GetEnvelopeAsync<HonuaReplicaManagementDetail>(
            $"/api/v1/admin/services/{Uri.EscapeDataString(serviceId)}/replicas/{Uri.EscapeDataString(replicaId)}",
            "GET /api/v1/admin/services/{serviceId}/replicas/{replicaId}",
            cancellationToken);
    }

    public Task<HonuaAdminEndpointResult<HonuaReplicaConflictListResponse>> ListReplicaConflictsAsync(
        string serviceId,
        string replicaId,
        string? status,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(replicaId);

        var queryString = string.IsNullOrWhiteSpace(status)
            ? string.Empty
            : $"?status={Uri.EscapeDataString(status)}";

        return GetEnvelopeAsync<HonuaReplicaConflictListResponse>(
            $"/api/v1/admin/services/{Uri.EscapeDataString(serviceId)}/replicas/{Uri.EscapeDataString(replicaId)}/conflicts{queryString}",
            "GET /api/v1/admin/services/{serviceId}/replicas/{replicaId}/conflicts",
            cancellationToken);
    }

    public Task<HonuaAdminEndpointResult<HonuaReplicaConflictDetail>> GetReplicaConflictAsync(
        string serviceId,
        string replicaId,
        string conflictId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(replicaId);
        ArgumentException.ThrowIfNullOrWhiteSpace(conflictId);

        return GetEnvelopeAsync<HonuaReplicaConflictDetail>(
            $"/api/v1/admin/services/{Uri.EscapeDataString(serviceId)}/replicas/{Uri.EscapeDataString(replicaId)}/conflicts/{Uri.EscapeDataString(conflictId)}",
            "GET /api/v1/admin/services/{serviceId}/replicas/{replicaId}/conflicts/{conflictId}",
            cancellationToken);
    }

    public Task<HonuaAdminEndpointResult<HonuaReplicaConflictResolutionResponse>> ResolveReplicaConflictAsync(
        string serviceId,
        string replicaId,
        string conflictId,
        HonuaReplicaConflictResolutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(replicaId);
        ArgumentException.ThrowIfNullOrWhiteSpace(conflictId);
        ArgumentNullException.ThrowIfNull(request);

        return PostEnvelopeAsync<HonuaReplicaConflictResolutionRequest, HonuaReplicaConflictResolutionResponse>(
            $"/api/v1/admin/services/{Uri.EscapeDataString(serviceId)}/replicas/{Uri.EscapeDataString(replicaId)}/conflicts/{Uri.EscapeDataString(conflictId)}/resolve",
            "POST /api/v1/admin/services/{serviceId}/replicas/{replicaId}/conflicts/{conflictId}/resolve",
            request,
            cancellationToken);
    }

    public void Dispose() => _httpClient.Dispose();

    private static string Layer(int layerId) => layerId.ToString(CultureInfo.InvariantCulture);

    // The temporal capability/as-of endpoints return the DTO body directly (not wrapped in ApiResponse<T>).
    private async Task<HonuaAdminEndpointResult<T>> GetDirectAsync<T>(
        string path,
        string contract,
        CancellationToken cancellationToken)
    {
        var (response, transportIssue) = await SendAsync(path, contract, cancellationToken).ConfigureAwait(false);
        if (transportIssue is not null)
        {
            return HonuaAdminEndpointResult<T>.FromIssue(transportIssue);
        }

        using var http = response!;
        if (!http.IsSuccessStatusCode)
        {
            return HonuaAdminEndpointResult<T>.FromIssue(CreateIssue(contract, http.StatusCode));
        }

        T? payload;
        try
        {
            payload = await http.Content
                .ReadFromJsonAsync<T>(JsonOptions, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            return HonuaAdminEndpointResult<T>.FromIssue(new HonuaAdminEndpointIssue(
                "Unsupported",
                contract,
                $"The Honua server temporal response did not match the expected contract: {ex.Message}",
                (int)http.StatusCode));
        }

        return payload is null
            ? HonuaAdminEndpointResult<T>.FromIssue(new HonuaAdminEndpointIssue(
                "Unavailable",
                contract,
                "The Honua server temporal response body was empty.",
                (int)http.StatusCode))
            : HonuaAdminEndpointResult<T>.FromData(payload);
    }

    // The temporal rollback plan/execute endpoints are POSTs that return their DTO body directly (not
    // wrapped in ApiResponse<T>). Plan returns 200; execute returns 202 Accepted with the job handle.
    private async Task<HonuaAdminEndpointResult<TResponse>> PostDirectAsync<TRequest, TResponse>(
        string path,
        string contract,
        TRequest body,
        CancellationToken cancellationToken)
    {
        var (response, transportIssue) = await SendAsync(
                HttpMethod.Post,
                path,
                contract,
                () => JsonContent.Create(body, options: JsonOptions),
                cancellationToken)
            .ConfigureAwait(false);
        if (transportIssue is not null)
        {
            return HonuaAdminEndpointResult<TResponse>.FromIssue(transportIssue);
        }

        using var http = response!;
        if (!http.IsSuccessStatusCode)
        {
            return HonuaAdminEndpointResult<TResponse>.FromIssue(CreateIssue(contract, http.StatusCode));
        }

        TResponse? payload;
        try
        {
            payload = await http.Content
                .ReadFromJsonAsync<TResponse>(JsonOptions, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            return HonuaAdminEndpointResult<TResponse>.FromIssue(new HonuaAdminEndpointIssue(
                "Unsupported",
                contract,
                $"The Honua server temporal response did not match the expected contract: {ex.Message}",
                (int)http.StatusCode));
        }

        return payload is null
            ? HonuaAdminEndpointResult<TResponse>.FromIssue(new HonuaAdminEndpointIssue(
                "Unavailable",
                contract,
                "The Honua server temporal response body was empty.",
                (int)http.StatusCode))
            : HonuaAdminEndpointResult<TResponse>.FromData(payload);
    }

    // The replica management endpoints wrap their payload in the shared ApiResponse<T> envelope.
    private async Task<HonuaAdminEndpointResult<T>> GetEnvelopeAsync<T>(
        string path,
        string contract,
        CancellationToken cancellationToken)
    {
        var (response, transportIssue) = await SendAsync(path, contract, cancellationToken).ConfigureAwait(false);
        if (transportIssue is not null)
        {
            return HonuaAdminEndpointResult<T>.FromIssue(transportIssue);
        }

        using var http = response!;
        if (!http.IsSuccessStatusCode)
        {
            return HonuaAdminEndpointResult<T>.FromIssue(CreateIssue(contract, http.StatusCode));
        }

        HonuaTemporalApiResponse<T>? envelope;
        try
        {
            envelope = await http.Content
                .ReadFromJsonAsync<HonuaTemporalApiResponse<T>>(JsonOptions, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            return HonuaAdminEndpointResult<T>.FromIssue(new HonuaAdminEndpointIssue(
                "Unsupported",
                contract,
                $"The Honua server response did not match the expected admin API shape: {ex.Message}",
                (int)http.StatusCode));
        }

        if (envelope?.Success == true && envelope.Data is not null)
        {
            return HonuaAdminEndpointResult<T>.FromData(envelope.Data);
        }

        return HonuaAdminEndpointResult<T>.FromIssue(new HonuaAdminEndpointIssue(
            "Unavailable",
            contract,
            envelope?.Message ?? "The Honua server response did not include data.",
            (int)http.StatusCode));
    }

    // The conflict-resolution endpoint is a POST that wraps its payload in the shared ApiResponse<T>
    // envelope. The request body is the operator-selected resolution; the response is the resolved
    // conflict detail plus whether a new committed server state was produced.
    private async Task<HonuaAdminEndpointResult<TResponse>> PostEnvelopeAsync<TRequest, TResponse>(
        string path,
        string contract,
        TRequest body,
        CancellationToken cancellationToken)
    {
        var (response, transportIssue) = await SendAsync(
                HttpMethod.Post,
                path,
                contract,
                () => JsonContent.Create(body, options: JsonOptions),
                cancellationToken)
            .ConfigureAwait(false);
        if (transportIssue is not null)
        {
            return HonuaAdminEndpointResult<TResponse>.FromIssue(transportIssue);
        }

        using var http = response!;
        if (!http.IsSuccessStatusCode)
        {
            return HonuaAdminEndpointResult<TResponse>.FromIssue(CreateIssue(contract, http.StatusCode));
        }

        HonuaTemporalApiResponse<TResponse>? envelope;
        try
        {
            envelope = await http.Content
                .ReadFromJsonAsync<HonuaTemporalApiResponse<TResponse>>(JsonOptions, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            return HonuaAdminEndpointResult<TResponse>.FromIssue(new HonuaAdminEndpointIssue(
                "Unsupported",
                contract,
                $"The Honua server response did not match the expected admin API shape: {ex.Message}",
                (int)http.StatusCode));
        }

        if (envelope?.Success == true && envelope.Data is not null)
        {
            return HonuaAdminEndpointResult<TResponse>.FromData(envelope.Data);
        }

        return HonuaAdminEndpointResult<TResponse>.FromIssue(new HonuaAdminEndpointIssue(
            "Unavailable",
            contract,
            envelope?.Message ?? "The Honua server response did not include data.",
            (int)http.StatusCode));
    }

    private Task<(HttpResponseMessage? Response, HonuaAdminEndpointIssue? Issue)> SendAsync(
        string path,
        string contract,
        CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Get, path, contract, content: null, cancellationToken);

    private async Task<(HttpResponseMessage? Response, HonuaAdminEndpointIssue? Issue)> SendAsync(
        HttpMethod method,
        string path,
        string contract,
        Func<HttpContent>? content,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        if (content is not null)
        {
            request.Content = content();
        }

        if (!string.IsNullOrWhiteSpace(_apiKey))
        {
            request.Headers.TryAddWithoutValidation("X-API-Key", _apiKey);
        }

        try
        {
            var response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            return (response, null);
        }
        catch (HttpRequestException ex)
        {
            return (null, new HonuaAdminEndpointIssue(
                "Unavailable",
                contract,
                $"The Honua server temporal endpoint could not be reached: {ex.Message}"));
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            return (null, new HonuaAdminEndpointIssue(
                "Unavailable",
                contract,
                $"The Honua server temporal endpoint could not be reached: {ex.Message}"));
        }
    }

    private static HonuaAdminEndpointIssue CreateIssue(string contract, HttpStatusCode statusCode)
    {
        // A 409 means different things by contract. On the conflict-resolution POST it is an
        // already-resolved conflict (the operator raced another resolver or reloaded stale state) — a
        // recoverable Conflict the caller can clear by reloading the queue. On capability/as-of reads it
        // is the server declaring the layer does not support the requested temporal capability.
        var conflictIsAlreadyResolved =
            statusCode == HttpStatusCode.Conflict
            && contract.EndsWith("/resolve", StringComparison.Ordinal);

        var state = statusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "Forbidden",
            HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotImplemented => "Unsupported",
            HttpStatusCode.Conflict => conflictIsAlreadyResolved ? "Conflict" : "Unsupported",
            HttpStatusCode.BadRequest => "Rejected",
            _ => "Unavailable"
        };

        var detail = statusCode switch
        {
            HttpStatusCode.Unauthorized => "The Honua server rejected the request because temporal-history read authentication is missing.",
            HttpStatusCode.Forbidden => "The Honua server rejected the request because the current principal lacks the temporal-history read entitlement.",
            HttpStatusCode.NotFound => "The Honua server temporal service, layer, or replica was not found.",
            HttpStatusCode.MethodNotAllowed => "The Honua server exposes the temporal route but not the required verb.",
            HttpStatusCode.NotImplemented => "The Honua server reports the temporal capability is not implemented.",
            HttpStatusCode.Conflict => conflictIsAlreadyResolved
                ? "The Honua server reports this conflict has already been resolved. Reload the conflict queue and retry against the current state."
                : "The Honua server reports this layer does not support the requested temporal capability.",
            HttpStatusCode.BadRequest => "The Honua server rejected the temporal request as invalid.",
            HttpStatusCode.ServiceUnavailable => "The Honua server temporal store is currently unavailable.",
            _ => string.Format(
                CultureInfo.InvariantCulture,
                "The Honua server returned HTTP {0} ({1}) for the temporal request.",
                (int)statusCode,
                statusCode)
        };

        return new HonuaAdminEndpointIssue(state, contract, detail, (int)statusCode);
    }
}

// --- ApiResponse envelope (mirrors Honua.Infrastructure.Models.ApiResponse<T> success body). ---

internal sealed record HonuaTemporalApiResponse<T>
{
    [JsonPropertyName("success")]
    public bool Success { get; init; }

    [JsonPropertyName("data")]
    public T? Data { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }
}

// --- Wire records mirroring honua-server temporal capability + as-of (TemporalHistoryApiModels). ---

public sealed record HonuaTemporalCapabilityResponse
{
    [JsonPropertyName("serviceId")]
    public string ServiceId { get; init; } = string.Empty;

    [JsonPropertyName("layerId")]
    public int LayerId { get; init; }

    [JsonPropertyName("layerName")]
    public string? LayerName { get; init; }

    [JsonPropertyName("supportsHistory")]
    public bool SupportsHistory { get; init; }

    [JsonPropertyName("supportsAsOf")]
    public bool SupportsAsOf { get; init; }

    [JsonPropertyName("temporalColumn")]
    public string? TemporalColumn { get; init; }

    [JsonPropertyName("cursorKind")]
    public string CursorKind { get; init; } = string.Empty;

    [JsonPropertyName("currentGeneration")]
    public long? CurrentGeneration { get; init; }

    [JsonPropertyName("deferred")]
    public HonuaTemporalDeferredCapabilities Deferred { get; init; } = new();
}

public sealed record HonuaTemporalDeferredCapabilities
{
    [JsonPropertyName("supportsDiff")]
    public bool SupportsDiff { get; init; }

    [JsonPropertyName("supportsTimeline")]
    public bool SupportsTimeline { get; init; }

    [JsonPropertyName("supportsAttribution")]
    public bool SupportsAttribution { get; init; }

    [JsonPropertyName("supportsRollback")]
    public bool SupportsRollback { get; init; }
}

public sealed record HonuaTemporalAsOfResponse
{
    [JsonPropertyName("serviceId")]
    public string ServiceId { get; init; } = string.Empty;

    [JsonPropertyName("layerId")]
    public int LayerId { get; init; }

    [JsonPropertyName("requestedCursorKind")]
    public string RequestedCursorKind { get; init; } = string.Empty;

    [JsonPropertyName("resolvedGeneration")]
    public long ResolvedGeneration { get; init; }

    [JsonPropertyName("currentGeneration")]
    public long CurrentGeneration { get; init; }

    [JsonPropertyName("features")]
    public HonuaTemporalFeatureState[] Features { get; init; } = [];
}

public sealed record HonuaTemporalFeatureState
{
    [JsonPropertyName("objectId")]
    public long ObjectId { get; init; }

    [JsonPropertyName("operation")]
    public string Operation { get; init; } = string.Empty;

    [JsonPropertyName("changedAt")]
    public string ChangedAt { get; init; } = string.Empty;

    [JsonPropertyName("attributes")]
    public Dictionary<string, JsonElement>? Attributes { get; init; }
}

// --- Wire records mirroring honua-server temporal diff/timeline/rollback (TemporalHistorySlicesApiModels,
//     #1166 slices 2-5, shipped as #1285). Bodies are camelCase and returned directly. ---

public sealed record HonuaTemporalCheckpointResponse
{
    [JsonPropertyName("kind")]
    public string Kind { get; init; } = string.Empty;

    [JsonPropertyName("value")]
    public string? Value { get; init; }

    [JsonPropertyName("generation")]
    public long Generation { get; init; }
}

public sealed record HonuaTemporalAttributionResponse
{
    [JsonPropertyName("actor")]
    public string? Actor { get; init; }

    [JsonPropertyName("source")]
    public string Source { get; init; } = string.Empty;

    [JsonPropertyName("operation")]
    public string? Operation { get; init; }

    [JsonPropertyName("sourceId")]
    public string? SourceId { get; init; }
}

public sealed record HonuaTemporalFieldChangeResponse
{
    [JsonPropertyName("field")]
    public string Field { get; init; } = string.Empty;

    [JsonPropertyName("oldValue")]
    public JsonElement? OldValue { get; init; }

    [JsonPropertyName("newValue")]
    public JsonElement? NewValue { get; init; }

    [JsonPropertyName("masked")]
    public bool Masked { get; init; }
}

public sealed record HonuaTemporalFeatureDiffResponse
{
    [JsonPropertyName("objectId")]
    public long ObjectId { get; init; }

    [JsonPropertyName("primaryClass")]
    public string PrimaryClass { get; init; } = string.Empty;

    [JsonPropertyName("classes")]
    public string[] Classes { get; init; } = [];

    [JsonPropertyName("geometryChanged")]
    public bool GeometryChanged { get; init; }

    [JsonPropertyName("fieldChanges")]
    public HonuaTemporalFieldChangeResponse[] FieldChanges { get; init; } = [];

    [JsonPropertyName("attribution")]
    public HonuaTemporalAttributionResponse? Attribution { get; init; }
}

public sealed record HonuaTemporalDiffSummaryResponse
{
    [JsonPropertyName("added")]
    public int Added { get; init; }

    [JsonPropertyName("removed")]
    public int Removed { get; init; }

    [JsonPropertyName("attributeChanged")]
    public int AttributeChanged { get; init; }

    [JsonPropertyName("geometryChanged")]
    public int GeometryChanged { get; init; }

    [JsonPropertyName("total")]
    public int Total { get; init; }
}

public sealed record HonuaTemporalDiffResponse
{
    [JsonPropertyName("serviceId")]
    public string ServiceId { get; init; } = string.Empty;

    [JsonPropertyName("layerId")]
    public int LayerId { get; init; }

    [JsonPropertyName("from")]
    public HonuaTemporalCheckpointResponse From { get; init; } = new();

    [JsonPropertyName("to")]
    public HonuaTemporalCheckpointResponse To { get; init; } = new();

    [JsonPropertyName("summary")]
    public HonuaTemporalDiffSummaryResponse Summary { get; init; } = new();

    [JsonPropertyName("changes")]
    public HonuaTemporalFeatureDiffResponse[] Changes { get; init; } = [];

    [JsonPropertyName("nextCursor")]
    public string? NextCursor { get; init; }
}

public sealed record HonuaTemporalRevisionResponse
{
    [JsonPropertyName("generation")]
    public long Generation { get; init; }

    [JsonPropertyName("operation")]
    public string Operation { get; init; } = string.Empty;

    [JsonPropertyName("changedAt")]
    public string ChangedAt { get; init; } = string.Empty;

    [JsonPropertyName("attribution")]
    public HonuaTemporalAttributionResponse? Attribution { get; init; }
}

public sealed record HonuaTemporalTimelineResponse
{
    [JsonPropertyName("serviceId")]
    public string ServiceId { get; init; } = string.Empty;

    [JsonPropertyName("layerId")]
    public int LayerId { get; init; }

    [JsonPropertyName("objectId")]
    public long ObjectId { get; init; }

    [JsonPropertyName("currentGeneration")]
    public long CurrentGeneration { get; init; }

    [JsonPropertyName("revisions")]
    public HonuaTemporalRevisionResponse[] Revisions { get; init; } = [];

    [JsonPropertyName("nextCursor")]
    public string? NextCursor { get; init; }
}

// A target checkpoint reference supplied in a rollback request body (kind + value/generation).
public sealed record HonuaTemporalCheckpointBody
{
    [JsonPropertyName("kind")]
    public string? Kind { get; init; }

    [JsonPropertyName("value")]
    public string? Value { get; init; }

    [JsonPropertyName("generation")]
    public long? Generation { get; init; }
}

public sealed record HonuaTemporalRollbackPlanRequest
{
    [JsonPropertyName("checkpoint")]
    public HonuaTemporalCheckpointBody? Checkpoint { get; init; }
}

public sealed record HonuaTemporalRollbackExecuteRequest
{
    [JsonPropertyName("checkpoint")]
    public HonuaTemporalCheckpointBody? Checkpoint { get; init; }

    [JsonPropertyName("approved")]
    public bool Approved { get; init; }

    [JsonPropertyName("reason")]
    public string? Reason { get; init; }
}

public sealed record HonuaTemporalRollbackFindingResponse
{
    [JsonPropertyName("code")]
    public string Code { get; init; } = string.Empty;

    [JsonPropertyName("severity")]
    public string Severity { get; init; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;
}

public sealed record HonuaTemporalRollbackPlanResponse
{
    [JsonPropertyName("serviceId")]
    public string ServiceId { get; init; } = string.Empty;

    [JsonPropertyName("layerId")]
    public int LayerId { get; init; }

    [JsonPropertyName("targetCheckpoint")]
    public HonuaTemporalCheckpointResponse TargetCheckpoint { get; init; } = new();

    [JsonPropertyName("currentGeneration")]
    public long CurrentGeneration { get; init; }

    [JsonPropertyName("state")]
    public string State { get; init; } = string.Empty;

    [JsonPropertyName("affectedFeatureCount")]
    public int AffectedFeatureCount { get; init; }

    [JsonPropertyName("validationFindings")]
    public HonuaTemporalRollbackFindingResponse[] ValidationFindings { get; init; } = [];

    [JsonPropertyName("compatibilityFindings")]
    public HonuaTemporalRollbackFindingResponse[] CompatibilityFindings { get; init; } = [];

    [JsonPropertyName("requiresApproval")]
    public bool RequiresApproval { get; init; }
}

public sealed record HonuaTemporalRollbackJobResponse
{
    [JsonPropertyName("jobId")]
    public string JobId { get; init; } = string.Empty;

    [JsonPropertyName("serviceId")]
    public string ServiceId { get; init; } = string.Empty;

    [JsonPropertyName("layerId")]
    public int LayerId { get; init; }

    [JsonPropertyName("targetCheckpoint")]
    public HonuaTemporalCheckpointResponse TargetCheckpoint { get; init; } = new();

    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;
}

// --- Wire records mirroring honua-server replica management (ReplicaManagementModels, #1167). ---

public sealed record HonuaReplicaManagementListResponse
{
    [JsonPropertyName("serviceId")]
    public string ServiceId { get; init; } = string.Empty;

    [JsonPropertyName("replicas")]
    public HonuaReplicaManagementSummary[] Replicas { get; init; } = [];
}

public sealed record HonuaReplicaManagementSummary
{
    [JsonPropertyName("replicaId")]
    public string ReplicaId { get; init; } = string.Empty;

    [JsonPropertyName("replicaName")]
    public string ReplicaName { get; init; } = string.Empty;

    [JsonPropertyName("serviceId")]
    public string ServiceId { get; init; } = string.Empty;

    [JsonPropertyName("syncModel")]
    public string SyncModel { get; init; } = string.Empty;

    [JsonPropertyName("layerIds")]
    public int[] LayerIds { get; init; } = [];

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; init; }

    [JsonPropertyName("lastSyncTime")]
    public DateTimeOffset LastSyncTime { get; init; }
}

public sealed record HonuaReplicaManagementDetail
{
    [JsonPropertyName("replicaId")]
    public string ReplicaId { get; init; } = string.Empty;

    [JsonPropertyName("replicaName")]
    public string ReplicaName { get; init; } = string.Empty;

    [JsonPropertyName("serviceId")]
    public string ServiceId { get; init; } = string.Empty;

    [JsonPropertyName("syncModel")]
    public string SyncModel { get; init; } = string.Empty;

    [JsonPropertyName("layerIds")]
    public int[] LayerIds { get; init; } = [];

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; init; }

    [JsonPropertyName("lastSyncTime")]
    public DateTimeOffset LastSyncTime { get; init; }

    [JsonPropertyName("lastSyncGeneration")]
    public long LastSyncGeneration { get; init; }
}

// --- Wire records mirroring honua-server replica conflict review (ReplicaConflictModels, #1167 slice 2). ---

public sealed record HonuaReplicaConflictListResponse
{
    [JsonPropertyName("serviceId")]
    public string ServiceId { get; init; } = string.Empty;

    [JsonPropertyName("replicaId")]
    public string ReplicaId { get; init; } = string.Empty;

    [JsonPropertyName("statusFilter")]
    public string? StatusFilter { get; init; }

    [JsonPropertyName("conflicts")]
    public HonuaReplicaConflictSummary[] Conflicts { get; init; } = [];
}

public sealed record HonuaReplicaConflictSummary
{
    [JsonPropertyName("conflictId")]
    public string ConflictId { get; init; } = string.Empty;

    [JsonPropertyName("replicaId")]
    public string ReplicaId { get; init; } = string.Empty;

    [JsonPropertyName("serviceId")]
    public string ServiceId { get; init; } = string.Empty;

    [JsonPropertyName("layerId")]
    public int LayerId { get; init; }

    [JsonPropertyName("objectId")]
    public long ObjectId { get; init; }

    [JsonPropertyName("conflictType")]
    public string ConflictType { get; init; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    [JsonPropertyName("serverGeneration")]
    public long ServerGeneration { get; init; }

    [JsonPropertyName("detectedAt")]
    public DateTimeOffset DetectedAt { get; init; }
}

public sealed record HonuaReplicaConflictDetail
{
    [JsonPropertyName("conflictId")]
    public string ConflictId { get; init; } = string.Empty;

    [JsonPropertyName("replicaId")]
    public string ReplicaId { get; init; } = string.Empty;

    [JsonPropertyName("serviceId")]
    public string ServiceId { get; init; } = string.Empty;

    [JsonPropertyName("layerId")]
    public int LayerId { get; init; }

    [JsonPropertyName("objectId")]
    public long ObjectId { get; init; }

    [JsonPropertyName("conflictType")]
    public string ConflictType { get; init; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    [JsonPropertyName("syncOperationId")]
    public string? SyncOperationId { get; init; }

    [JsonPropertyName("deviceId")]
    public string? DeviceId { get; init; }

    [JsonPropertyName("userId")]
    public string? UserId { get; init; }

    [JsonPropertyName("serverGeneration")]
    public long ServerGeneration { get; init; }

    [JsonPropertyName("baseState")]
    public JsonElement? BaseState { get; init; }

    [JsonPropertyName("clientState")]
    public JsonElement? ClientState { get; init; }

    [JsonPropertyName("serverState")]
    public JsonElement? ServerState { get; init; }

    [JsonPropertyName("detectedAt")]
    public DateTimeOffset DetectedAt { get; init; }

    [JsonPropertyName("resolutionAction")]
    public string? ResolutionAction { get; init; }

    [JsonPropertyName("resolvedBy")]
    public string? ResolvedBy { get; init; }

    [JsonPropertyName("resolvedAt")]
    public DateTimeOffset? ResolvedAt { get; init; }

    [JsonPropertyName("resolvedServerGeneration")]
    public long? ResolvedServerGeneration { get; init; }
}

public sealed record HonuaReplicaConflictResolutionRequest
{
    [JsonPropertyName("action")]
    public string? Action { get; init; }
}

public sealed record HonuaReplicaConflictResolutionResponse
{
    [JsonPropertyName("conflict")]
    public HonuaReplicaConflictDetail? Conflict { get; init; }

    [JsonPropertyName("committedNewServerState")]
    public bool CommittedNewServerState { get; init; }
}
