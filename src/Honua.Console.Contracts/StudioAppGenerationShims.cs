using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Honua.Console.Contracts;

// SHIM(honua-server app generation / honua-sdk-dotnet): honua-server owns the natural-language ->
// app.package generation contract (POST /api/v1/studio/app-packages/generate), a provider-pluggable planner
// grounded in the catalog/saved-content and gated by the app-package validator, mirroring the map generation
// contract (StudioMapGenerationShims). honua-sdk-dotnet does not yet project this as a consumable stable
// package and honua-console wires no SDK NuGet feed (SDK_SHIM_POLICY.md), so the wire records + thin HTTP
// client live behind this single Honua.Console.Contracts boundary. Do not add a sibling-repo
// ProjectReference. KEY SIMPLIFICATION vs map: the server returns the SAME studio-app/v1 body the console
// authors/round-trips, so the data source hydrates the editor with the EXISTING
// StudioAppPackageMapper.ApplyEnvelopeBody — no generation-specific mapper. The generation endpoint returns
// the BARE result object via Results.Json — NOT wrapped in the StudioApiResponse {success, data} envelope the
// package lifecycle endpoints use. Until the server ships this endpoint it returns 404 -> the console renders
// the honest "AI generation unavailable" state (no fabricated app). camelCase props.

#region App generation DTOs

public sealed record AppGenerationClarificationChoice
{
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;
    [JsonPropertyName("label")] public string Label { get; init; } = string.Empty;
    [JsonPropertyName("effect")] public string? Effect { get; init; }
}

/// <summary>A structured question the planner needs answered before it can propose an app. The console
/// renders these as selectable cards (flattened projection of the server's clarification envelope).</summary>
public sealed record AppGenerationClarification
{
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;
    /// <summary>"page" | "component" | "binding" | "permission" | ...</summary>
    [JsonPropertyName("kind")] public string Kind { get; init; } = string.Empty;
    [JsonPropertyName("prompt")] public string Prompt { get; init; } = string.Empty;
    [JsonPropertyName("reason")] public string? Reason { get; init; }
    [JsonPropertyName("choices")] public IReadOnlyList<AppGenerationClarificationChoice> Choices { get; init; } = [];
}

/// <summary>AI-builder capability state for a requested operation (supported/degraded/unsupported/...).</summary>
public sealed record AppGenerationCapabilityState
{
    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
    [JsonPropertyName("state")] public string State { get; init; } = string.Empty;
    [JsonPropertyName("reason")] public string? Reason { get; init; }
}

public sealed record AppGenerationValidationFailure
{
    [JsonPropertyName("code")] public string Code { get; init; } = string.Empty;
    [JsonPropertyName("message")] public string Message { get; init; } = string.Empty;
    [JsonPropertyName("fieldPath")] public string? FieldPath { get; init; }
}

public sealed record AppGenerationValidationResult
{
    [JsonPropertyName("isValid")] public bool IsValid { get; init; }
    [JsonPropertyName("failures")] public IReadOnlyList<AppGenerationValidationFailure> Failures { get; init; } = [];
    [JsonPropertyName("warnings")] public IReadOnlyList<string> Warnings { get; init; } = [];
}

/// <summary>Result of POST /studio/app-packages/generate. <c>status</c> mirrors the plan-analysis statuses:
/// "generated" | "needs-clarification" | "unsupported" | "refused" | "error". <c>package</c> is the
/// studio-app/v1 envelope body (the same shape StudioAppPackageMapper.ApplyEnvelopeBody consumes), present
/// iff status=="generated".</summary>
public sealed record AppGenerationResult
{
    [JsonPropertyName("status")] public string Status { get; init; } = string.Empty;
    [JsonPropertyName("package")] public JsonElement? Package { get; init; }
    [JsonPropertyName("rationale")] public string? Rationale { get; init; }
    [JsonPropertyName("clarifications")] public IReadOnlyList<AppGenerationClarification> Clarifications { get; init; } = [];
    [JsonPropertyName("validation")] public AppGenerationValidationResult? Validation { get; init; }
    [JsonPropertyName("unmappedRequests")] public IReadOnlyList<string> UnmappedRequests { get; init; } = [];
    [JsonPropertyName("capabilityState")] public AppGenerationCapabilityState? CapabilityState { get; init; }
    [JsonPropertyName("provider")] public string? Provider { get; init; }
    [JsonPropertyName("model")] public string? Model { get; init; }
}

public sealed record AppGenerationAnswer
{
    [JsonPropertyName("questionId")] public string QuestionId { get; init; } = string.Empty;
    [JsonPropertyName("optionId")] public string OptionId { get; init; } = string.Empty;
}

public sealed record AppGenerationTurn
{
    /// <summary>"user" | "assistant".</summary>
    [JsonPropertyName("role")] public string Role { get; init; } = string.Empty;
    [JsonPropertyName("content")] public string Content { get; init; } = string.Empty;
}

public sealed record GenerateAppPackageRequest
{
    [JsonPropertyName("prompt")] public string Prompt { get; init; } = string.Empty;
    /// <summary>Provider id to use; null selects the server default.</summary>
    [JsonPropertyName("provider")] public string? Provider { get; init; }
    /// <summary>Optional per-call model override.</summary>
    [JsonPropertyName("model")] public string? Model { get; init; }
    /// <summary>Current studio-app/v1 body for a REFINE turn; null requests fresh generation.</summary>
    [JsonPropertyName("package")] public JsonElement? Package { get; init; }
    [JsonPropertyName("conversation")] public IReadOnlyList<AppGenerationTurn> Conversation { get; init; } = [];
    /// <summary>Answers to a prior needs-clarification turn.</summary>
    [JsonPropertyName("answers")] public IReadOnlyList<AppGenerationAnswer> Answers { get; init; } = [];
}

#endregion

#region Typed client

public sealed record StudioAppGenerationClientOptions(Uri BaseUri, string? ApiKey = null);

/// <summary>
/// Thin typed client over the honua-server app-package generation endpoint (POST
/// /api/v1/studio/app-packages/generate). Mirrors <see cref="IStudioMapGenerationClient"/>: every call
/// returns a <see cref="StudioEndpointResult{T}"/> so the app builder renders the shared
/// blocked/missing-binding state instead of throwing or fabricating data.
/// </summary>
public interface IStudioAppGenerationClient
{
    Uri BaseUri { get; }

    /// <summary>Generates (or refines) an app.package from a natural-language prompt.</summary>
    Task<StudioEndpointResult<AppGenerationResult>> GenerateAppAsync(
        GenerateAppPackageRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class HttpStudioAppGenerationClient : IStudioAppGenerationClient, IDisposable
{
    private const string StudioRoot = "/api/v1/studio";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly string? _apiKey;

    public HttpStudioAppGenerationClient(HttpClient httpClient, StudioAppGenerationClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);

        BaseUri = options.BaseUri;
        _apiKey = options.ApiKey;
        _httpClient = httpClient;
        _httpClient.BaseAddress ??= BaseUri;
    }

    public Uri BaseUri { get; }

    public Task<StudioEndpointResult<AppGenerationResult>> GenerateAppAsync(
        GenerateAppPackageRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SendAsync<GenerateAppPackageRequest, AppGenerationResult>(
            HttpMethod.Post,
            $"{StudioRoot}/app-packages/generate",
            request,
            "POST /api/v1/studio/app-packages/generate",
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
                $"The Honua server app generation endpoint could not be reached: {ex.Message}"));
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            return StudioEndpointResult<TResponse>.FromIssue(new StudioEndpointIssue(
                "Unavailable",
                contract,
                $"The Honua server app generation endpoint could not be reached: {ex.Message}"));
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
            // lifecycle endpoints use. Deserialize the payload directly (mirrors the map/query/dashboard
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
                    $"The Honua server app generation response did not match the expected API shape: {ex.Message}",
                    (int)response.StatusCode));
            }

            if (payload is not null)
            {
                return StudioEndpointResult<TResponse>.FromData(payload);
            }

            return StudioEndpointResult<TResponse>.FromIssue(new StudioEndpointIssue(
                "Unavailable",
                contract,
                "The Honua server app generation response was empty.",
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
            HttpStatusCode.Unauthorized => "The Honua server rejected the app generation request because admin authentication is missing.",
            HttpStatusCode.Forbidden => "The Honua server rejected the app generation request because the current principal lacks admin permission.",
            HttpStatusCode.NotFound => "The Honua server does not expose the app generation contract.",
            HttpStatusCode.MethodNotAllowed => "The Honua server exposes the route but not the required app generation verb.",
            HttpStatusCode.NotImplemented => "The Honua server reports app generation is not implemented.",
            HttpStatusCode.BadRequest => "The Honua server rejected the app generation request as invalid.",
            _ => string.Format(
                CultureInfo.InvariantCulture,
                "The Honua server returned HTTP {0} ({1}) for the app generation request.",
                (int)statusCode,
                statusCode)
        };

        return new StudioEndpointIssue(state, contract, detail, (int)statusCode);
    }
}

#endregion
