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

    private readonly HttpClient _httpClient;
    private readonly StudioGenerationHttpInvoker _invoker;

    public HttpStudioAppGenerationClient(HttpClient httpClient, StudioAppGenerationClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);

        BaseUri = options.BaseUri;
        _httpClient = httpClient;
        _httpClient.BaseAddress ??= BaseUri;
        _invoker = new StudioGenerationHttpInvoker(_httpClient, options.ApiKey, "app generation");
    }

    public Uri BaseUri { get; }

    public Task<StudioEndpointResult<AppGenerationResult>> GenerateAppAsync(
        GenerateAppPackageRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _invoker.SendAsync<GenerateAppPackageRequest, AppGenerationResult>(
            HttpMethod.Post,
            $"{StudioRoot}/app-packages/generate",
            request,
            "POST /api/v1/studio/app-packages/generate",
            cancellationToken);
    }

    public void Dispose() => _httpClient.Dispose();
}

#endregion
