using System.Text.Json.Serialization;

namespace Honua.Console.Contracts;

// SHIM(honua-sdk-dotnet): The honua-server deterministic ops-findings endpoints
// (Features/Admin/OpsObservabilityEndpoints.cs, group
// "/api/v{version}/admin/observability", admin-authorized via X-API-Key) are not yet
// projected to honua-sdk-dotnet. These records mirror the server HTTP wire surface
// (OpsObservabilityModels.cs) and are consumed through the single Console shim
// boundary. Findings are DETERMINISTIC server output — no model/LLM reasoning is
// involved here or server-side (ADR-0028); a finding's recommended action is proposed
// by id through the existing operation-gateway approval flow.
//
// IMPORTANT: handlers return BARE Results.Json payloads (NO ApiResponse envelope).
// JSON on the wire is camelCase; null-valued properties are omitted. The propose
// endpoint returns a 404 problem when the finding vanished (its condition cleared) or
// carries no recommended action.
//
// Routes (concrete v1), admin-authorized (X-API-Key):
//   GET  /api/v1/admin/observability/findings                    -> OpsFindingsListResponse
//   POST /api/v1/admin/observability/findings/{findingId}/propose -> OpsFindingProposeResponse (404 on cleared/no-action)

/// <summary>
/// Concrete v1 routes for the honua-server ops-findings endpoints, kept in one place so
/// the client and tests share the exact server paths. Relative (no leading slash); the
/// client's BuildUri normalizes.
/// </summary>
public static class OpsFindingsRoutes
{
    /// <summary>The findings list route.</summary>
    [OpsParityRoute("GET")]
    public const string List = "api/v1/admin/observability/findings";

    /// <summary>The route template for proposing a finding's recommended action.</summary>
    [OpsParityRoute("POST")]
    public const string ProposeTemplate = List + "/{findingId}/propose";

    /// <summary>Builds the propose-action route for a specific finding id.</summary>
    /// <param name="findingId">The deterministic finding identifier.</param>
    /// <returns>The relative propose route.</returns>
    public static string Propose(string findingId) =>
        ProposeTemplate.Replace("{findingId}", Uri.EscapeDataString(findingId), StringComparison.Ordinal);
}

/// <summary>
/// Response for <c>GET /api/v1/admin/observability/findings</c>. Mirrors the server
/// <c>OpsFindingsListResponse</c>.
/// </summary>
public sealed class OpsFindingsListResponse
{
    /// <summary>Gets or sets the UTC time the findings were evaluated.</summary>
    [JsonPropertyName("generatedAt")]
    public DateTimeOffset GeneratedAt { get; set; }

    /// <summary>Gets or sets the active findings, ordered by descending severity.</summary>
    [JsonPropertyName("findings")]
    public List<OpsFindingResponse>? Findings { get; set; }
}

/// <summary>Wire model of a single deterministic ops finding.</summary>
public sealed class OpsFindingResponse
{
    /// <summary>Gets or sets the deterministic finding identifier (used to propose its action).</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>Gets or sets the kebab-case rule identifier.</summary>
    [JsonPropertyName("rule")]
    public string? Rule { get; set; }

    /// <summary>Gets or sets the severity (Info/Warning/Critical).</summary>
    [JsonPropertyName("severity")]
    public string? Severity { get; set; }

    /// <summary>Gets or sets the short title.</summary>
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    /// <summary>Gets or sets the plain-language explanation.</summary>
    [JsonPropertyName("explanation")]
    public string? Explanation { get; set; }

    /// <summary>Gets or sets the detection (evaluation) time.</summary>
    [JsonPropertyName("detectedAt")]
    public DateTimeOffset DetectedAt { get; set; }

    /// <summary>Gets or sets the subject identifiers.</summary>
    [JsonPropertyName("subject")]
    public OpsFindingSubjectResponse? Subject { get; set; }

    /// <summary>Gets or sets the supporting evidence references.</summary>
    [JsonPropertyName("evidenceRefs")]
    public List<string>? EvidenceRefs { get; set; }

    /// <summary>Gets or sets the recommended action, or null when the finding is informational.</summary>
    [JsonPropertyName("recommendedAction")]
    public OpsFindingActionResponse? RecommendedAction { get; set; }
}

/// <summary>Subject identifiers a finding pins to.</summary>
public sealed class OpsFindingSubjectResponse
{
    /// <summary>Gets or sets the deploy target identifier, when applicable.</summary>
    [JsonPropertyName("targetId")]
    public string? TargetId { get; set; }

    /// <summary>Gets or sets the execution workload identifier, when applicable.</summary>
    [JsonPropertyName("workloadId")]
    public string? WorkloadId { get; set; }

    /// <summary>Gets or sets the notification channel identifier, when applicable.</summary>
    [JsonPropertyName("channel")]
    public string? Channel { get; set; }

    /// <summary>Gets or sets the workflow/deploy operation identifier, when applicable.</summary>
    [JsonPropertyName("operationId")]
    public string? OperationId { get; set; }

    /// <summary>Gets or sets the platform release version, when applicable.</summary>
    [JsonPropertyName("releaseVersion")]
    public string? ReleaseVersion { get; set; }
}

/// <summary>Wire model of a finding's recommended action.</summary>
public sealed class OpsFindingActionResponse
{
    /// <summary>Gets or sets the operation class the action routes through.</summary>
    [JsonPropertyName("kind")]
    public string? Kind { get; set; }

    /// <summary>Gets or sets the plain-language action summary.</summary>
    [JsonPropertyName("summary")]
    public string? Summary { get; set; }

    /// <summary>Gets or sets the operator-facing reason recorded on the proposal.</summary>
    [JsonPropertyName("reason")]
    public string? Reason { get; set; }
}

/// <summary>
/// Response for <c>POST /api/v1/admin/observability/findings/{findingId}/propose</c>.
/// Mirrors the server <c>OpsFindingProposeResponse</c>.
/// </summary>
public sealed class OpsFindingProposeResponse
{
    /// <summary>Gets or sets the finding identifier that was proposed.</summary>
    [JsonPropertyName("findingId")]
    public string? FindingId { get; set; }

    /// <summary>Gets or sets the proposal outcome status (Executed/ProposalCreated/Blocked/NotSupported).</summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    /// <summary>Gets or sets the created proposal identifier when the gateway required approval.</summary>
    [JsonPropertyName("proposalId")]
    public string? ProposalId { get; set; }

    /// <summary>Gets or sets the executed operation identifier when the gateway executed directly.</summary>
    [JsonPropertyName("executionOperationId")]
    public string? ExecutionOperationId { get; set; }

    /// <summary>Gets or sets an operator-facing message describing the outcome, when available.</summary>
    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

/// <summary>
/// Source-generated JSON context for the ops-findings wire contracts (trim/AOT safe),
/// mirroring MonitoringMetricsJsonContext.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(OpsFindingsListResponse))]
[JsonSerializable(typeof(OpsFindingProposeResponse))]
public sealed partial class OpsFindingsJsonContext : JsonSerializerContext
{
}
