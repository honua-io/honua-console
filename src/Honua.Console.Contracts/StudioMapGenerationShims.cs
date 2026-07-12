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
    /// <summary>Real published layers in the workspace (catalog grounding) so the model binds real data.</summary>
    [JsonPropertyName("availableSources")] public IReadOnlyList<MapGenerationSource> AvailableSources { get; init; } = [];
}

/// <summary>One real, published source the model may bind directly (catalog grounding).</summary>
public sealed record MapGenerationSource
{
    [JsonPropertyName("serviceId")] public string ServiceId { get; init; } = string.Empty;
    [JsonPropertyName("layerId")] public string LayerId { get; init; } = string.Empty;
    [JsonPropertyName("name")] public string? Name { get; init; }
    [JsonPropertyName("geometryType")] public string? GeometryType { get; init; }
    [JsonPropertyName("protocol")] public string? Protocol { get; init; }
    /// <summary>Layer extent [minLng, minLat, maxLng, maxLat] in EPSG:4326.</summary>
    [JsonPropertyName("bbox")] public double[]? Bbox { get; init; }
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

    private readonly HttpClient _httpClient;
    private readonly StudioGenerationHttpInvoker _invoker;

    public HttpStudioMapGenerationClient(HttpClient httpClient, StudioMapGenerationClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);

        BaseUri = options.BaseUri;
        _httpClient = httpClient;
        _httpClient.BaseAddress ??= BaseUri;
        _invoker = new StudioGenerationHttpInvoker(_httpClient, options.ApiKey, "map generation");
    }

    public Uri BaseUri { get; }

    public Task<StudioEndpointResult<MapGenerationResult>> GenerateMapAsync(
        GenerateMapPackageRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _invoker.SendAsync<GenerateMapPackageRequest, MapGenerationResult>(
            HttpMethod.Post,
            $"{StudioRoot}/map-packages/generate",
            request,
            "POST /api/v1/studio/map-packages/generate",
            cancellationToken);
    }

    public void Dispose() => _httpClient.Dispose();
}

#endregion
