using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Honua.Console.Contracts;

// SHIM(honua-server map generation / honua-sdk-dotnet): honua-server owns the natural-language ->
// map.package generation contract (POST /api/v1/studio/map-packages/generate), a provider-pluggable planner
// (local GIS model default, Claude + GPT options) grounded in the catalog and gated by the map-package
// validator, mirroring the workflow generation contract (StudioWorkflowShims). honua-sdk-dotnet does not yet
// project this as a consumable stable package and honua-console wires no SDK NuGet feed (SDK_SHIM_POLICY.md),
// so the wire records + thin HTTP client live behind this single Honua.Console.Contracts boundary. Do not add
// a sibling-repo ProjectReference. The Studio endpoint shares the {success,data,message} ApiResponse envelope
// (StudioApiResponse<T>) and the StudioEndpointResult/StudioEndpointIssue surfaces with the rest of the Studio
// package lifecycle (StudioPackageShims). Until the server ships this endpoint it returns 404 -> the console
// renders the honest "AI generation unavailable" state (no fabricated map). camelCase props.

#region Map generation DTOs

public sealed record MapGenerationClarificationChoice
{
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;
    [JsonPropertyName("label")] public string Label { get; init; } = string.Empty;
    [JsonPropertyName("effect")] public string? Effect { get; init; }
}

/// <summary>A structured question the planner needs answered before it can propose a map. The console
/// renders these as selectable cards (flattened projection of the server's clarification envelope).</summary>
public sealed record MapGenerationClarification
{
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;
    /// <summary>"layer" | "style" | "extent" | "basemap" | ...</summary>
    [JsonPropertyName("kind")] public string Kind { get; init; } = string.Empty;
    [JsonPropertyName("prompt")] public string Prompt { get; init; } = string.Empty;
    [JsonPropertyName("reason")] public string? Reason { get; init; }
    [JsonPropertyName("choices")] public IReadOnlyList<MapGenerationClarificationChoice> Choices { get; init; } = [];
}

/// <summary>AI-builder capability state for a requested operation (supported/degraded/unsupported/...).</summary>
public sealed record MapGenerationCapabilityState
{
    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
    [JsonPropertyName("state")] public string State { get; init; } = string.Empty;
    [JsonPropertyName("reason")] public string? Reason { get; init; }
}

public sealed record MapGenerationValidationFailure
{
    [JsonPropertyName("code")] public string Code { get; init; } = string.Empty;
    [JsonPropertyName("message")] public string Message { get; init; } = string.Empty;
    [JsonPropertyName("fieldPath")] public string? FieldPath { get; init; }
}

public sealed record MapGenerationValidationResult
{
    [JsonPropertyName("isValid")] public bool IsValid { get; init; }
    [JsonPropertyName("failures")] public IReadOnlyList<MapGenerationValidationFailure> Failures { get; init; } = [];
    [JsonPropertyName("warnings")] public IReadOnlyList<string> Warnings { get; init; } = [];
}

/// <summary>Result of POST /studio/map-packages/generate. <c>status</c> mirrors the plan-analysis statuses:
/// "generated" | "needs-clarification" | "unsupported" | "refused" | "error". <c>package</c> is the
/// map.package envelope body (the same shape StudioMapPackageMapper.ApplyEnvelopeBody consumes), present iff
/// status=="generated".</summary>
public sealed record MapGenerationResult
{
    [JsonPropertyName("status")] public string Status { get; init; } = string.Empty;
    [JsonPropertyName("package")] public JsonElement? Package { get; init; }
    [JsonPropertyName("rationale")] public string? Rationale { get; init; }
    [JsonPropertyName("clarifications")] public IReadOnlyList<MapGenerationClarification> Clarifications { get; init; } = [];
    [JsonPropertyName("validation")] public MapGenerationValidationResult? Validation { get; init; }
    [JsonPropertyName("unmappedRequests")] public IReadOnlyList<string> UnmappedRequests { get; init; } = [];
    [JsonPropertyName("capabilityState")] public MapGenerationCapabilityState? CapabilityState { get; init; }
    [JsonPropertyName("provider")] public string? Provider { get; init; }
    [JsonPropertyName("model")] public string? Model { get; init; }
}

public sealed record MapGenerationAnswer
{
    [JsonPropertyName("questionId")] public string QuestionId { get; init; } = string.Empty;
    [JsonPropertyName("optionId")] public string OptionId { get; init; } = string.Empty;
}

public sealed record MapGenerationTurn
{
    /// <summary>"user" | "assistant".</summary>
    [JsonPropertyName("role")] public string Role { get; init; } = string.Empty;
    [JsonPropertyName("content")] public string Content { get; init; } = string.Empty;
}

public sealed record GenerateMapPackageRequest
{
    [JsonPropertyName("prompt")] public string Prompt { get; init; } = string.Empty;
    /// <summary>Provider id to use; null selects the server default.</summary>
    [JsonPropertyName("provider")] public string? Provider { get; init; }
    /// <summary>Optional per-call model override.</summary>
    [JsonPropertyName("model")] public string? Model { get; init; }
    /// <summary>Current map.package body for a REFINE turn; null requests fresh generation.</summary>
    [JsonPropertyName("package")] public JsonElement? Package { get; init; }
    [JsonPropertyName("conversation")] public IReadOnlyList<MapGenerationTurn> Conversation { get; init; } = [];
    /// <summary>Answers to a prior needs-clarification turn.</summary>
    [JsonPropertyName("answers")] public IReadOnlyList<MapGenerationAnswer> Answers { get; init; } = [];
}

#endregion

#region Typed client

public sealed record StudioMapGenerationClientOptions(Uri BaseUri, string? ApiKey = null);

/// <summary>
/// Thin typed client over the honua-server map-package generation endpoint (POST
/// /api/v1/studio/map-packages/generate). Mirrors <see cref="IWorkflowPackageApiClient"/>: every call returns
/// a <see cref="StudioEndpointResult{T}"/> so the map builder renders the shared blocked/missing-binding state
/// instead of throwing or fabricating data.
/// </summary>
public interface IStudioMapGenerationClient
{
    Uri BaseUri { get; }

    /// <summary>Generates (or refines) a map.package from a natural-language prompt.</summary>
    Task<StudioEndpointResult<MapGenerationResult>> GenerateMapAsync(
        GenerateMapPackageRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class HttpStudioMapGenerationClient : IStudioMapGenerationClient, IDisposable
{
    private const string StudioRoot = "/api/v1/studio";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly string? _apiKey;

    public HttpStudioMapGenerationClient(HttpClient httpClient, StudioMapGenerationClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);

        BaseUri = options.BaseUri;
        _apiKey = options.ApiKey;
        _httpClient = httpClient;
        _httpClient.BaseAddress ??= BaseUri;
    }

    public Uri BaseUri { get; }

    public Task<StudioEndpointResult<MapGenerationResult>> GenerateMapAsync(
        GenerateMapPackageRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SendAsync<GenerateMapPackageRequest, MapGenerationResult>(
            HttpMethod.Post,
            $"{StudioRoot}/map-packages/generate",
            request,
            "POST /api/v1/studio/map-packages/generate",
            cancellationToken);
    }

    public void Dispose() => _httpClient.Dispose();

    private async Task<StudioEndpointResult<TResponse>> SendAsync<TBody, TResponse>(
        HttpMethod method,
        string path,
        TBody? body,
        string contract,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        if (!string.IsNullOrWhiteSpace(_apiKey))
        {
            request.Headers.TryAddWithoutValidation("X-API-Key", _apiKey);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType(), options: JsonOptions);
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
            return StudioEndpointResult<TResponse>.FromIssue(new StudioEndpointIssue(
                "Unavailable",
                contract,
                $"The Honua server map generation endpoint could not be reached: {ex.Message}"));
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            return StudioEndpointResult<TResponse>.FromIssue(new StudioEndpointIssue(
                "Unavailable",
                contract,
                $"The Honua server map generation endpoint could not be reached: {ex.Message}"));
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var serverDetail = await TryReadErrorDetailAsync(response, cancellationToken).ConfigureAwait(false);
                return StudioEndpointResult<TResponse>.FromIssue(CreateIssue(contract, response.StatusCode, serverDetail));
            }

            // The generation endpoints return the BARE result object ({status, package, rationale, ...}) via
            // Results.Json — NOT wrapped in the StudioApiResponse {success, data} envelope the package
            // lifecycle endpoints use. Deserialize the payload directly (mirrors the query/dashboard
            // generation clients); treating it as an envelope yields a null Data and a false "no data" error.
            TResponse? payload;
            try
            {
                payload = await response.Content
                    .ReadFromJsonAsync<TResponse>(JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (JsonException ex)
            {
                return StudioEndpointResult<TResponse>.FromIssue(new StudioEndpointIssue(
                    "Unsupported",
                    contract,
                    $"The Honua server map generation response did not match the expected API shape: {ex.Message}",
                    (int)response.StatusCode));
            }

            if (payload is not null)
            {
                return StudioEndpointResult<TResponse>.FromData(payload);
            }

            return StudioEndpointResult<TResponse>.FromIssue(new StudioEndpointIssue(
                "Unavailable",
                contract,
                "The Honua server map generation response was empty.",
                (int)response.StatusCode));
        }
    }

    private static async Task<string?> TryReadErrorDetailAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            var raw = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            using var document = JsonDocument.Parse(raw);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            return root.TryGetProperty("message", out var messageElement) &&
                messageElement.ValueKind == JsonValueKind.String
                ? messageElement.GetString()
                : root.TryGetProperty("detail", out var detailElement) && detailElement.ValueKind == JsonValueKind.String
                    ? detailElement.GetString()
                    : null;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            return null;
        }
    }

    private static StudioEndpointIssue CreateIssue(string contract, HttpStatusCode statusCode, string? serverDetail)
    {
        var state = statusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "Missing permission",
            HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotImplemented => "Unsupported",
            HttpStatusCode.BadRequest => "Validation failed",
            HttpStatusCode.Conflict => "Conflict",
            _ => "Unavailable"
        };

        var detail = serverDetail ?? statusCode switch
        {
            HttpStatusCode.Unauthorized => "The Honua server rejected the map generation request because admin authentication is missing.",
            HttpStatusCode.Forbidden => "The Honua server rejected the map generation request because the current principal lacks admin permission.",
            HttpStatusCode.NotFound => "The Honua server does not expose the map generation contract.",
            HttpStatusCode.MethodNotAllowed => "The Honua server exposes the route but not the required map generation verb.",
            HttpStatusCode.NotImplemented => "The Honua server reports map generation is not implemented.",
            HttpStatusCode.BadRequest => "The Honua server rejected the map generation request as invalid.",
            _ => string.Format(
                CultureInfo.InvariantCulture,
                "The Honua server returned HTTP {0} ({1}) for the map generation request.",
                (int)statusCode,
                statusCode)
        };

        return new StudioEndpointIssue(state, contract, detail, (int)statusCode);
    }
}

#endregion
