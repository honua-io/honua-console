using System.Text.Json.Serialization;

namespace Honua.Console.Contracts;

// The server returns a bare payload, omits null properties, and answers 404 when a propose target is gone or has no action.
public static class OpsFindingsRoutes
{
    [OpsParityRoute("GET")]
    public const string List = "api/v1/admin/observability/findings";

    [OpsParityRoute("POST")]
    public const string ProposeTemplate = List + "/{findingId}/propose";

    public static string Propose(string findingId) =>
        ProposeTemplate.Replace("{findingId}", Uri.EscapeDataString(findingId), StringComparison.Ordinal);
}

public sealed class OpsFindingsListResponse
{
    [JsonPropertyName("generatedAt")]
    public DateTimeOffset GeneratedAt { get; set; }

    [JsonPropertyName("findings")]
    public List<OpsFindingResponse>? Findings { get; set; }
}

public sealed class OpsFindingResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("rule")]
    public string? Rule { get; set; }

    [JsonPropertyName("severity")]
    public string? Severity { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("explanation")]
    public string? Explanation { get; set; }

    [JsonPropertyName("detectedAt")]
    public DateTimeOffset DetectedAt { get; set; }

    [JsonPropertyName("subject")]
    public OpsFindingSubjectResponse? Subject { get; set; }

    [JsonPropertyName("evidenceRefs")]
    public List<string>? EvidenceRefs { get; set; }

    // Null when the finding is informational and has nothing to propose.
    [JsonPropertyName("recommendedAction")]
    public OpsFindingActionResponse? RecommendedAction { get; set; }
}

public sealed class OpsFindingSubjectResponse
{
    [JsonPropertyName("targetId")]
    public string? TargetId { get; set; }

    [JsonPropertyName("workloadId")]
    public string? WorkloadId { get; set; }

    [JsonPropertyName("channel")]
    public string? Channel { get; set; }

    [JsonPropertyName("operationId")]
    public string? OperationId { get; set; }

    [JsonPropertyName("releaseVersion")]
    public string? ReleaseVersion { get; set; }

    [JsonPropertyName("protocol")]
    public string? Protocol { get; set; }
}

public sealed class OpsFindingActionResponse
{
    [JsonPropertyName("kind")]
    public string? Kind { get; set; }

    [JsonPropertyName("summary")]
    public string? Summary { get; set; }

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    [JsonPropertyName("autoSafe")]
    public bool AutoSafe { get; set; }

    [JsonPropertyName("blastRadius")]
    public int BlastRadius { get; set; } = 1;
}

public sealed class OpsFindingProposeResponse
{
    [JsonPropertyName("findingId")]
    public string? FindingId { get; set; }

    // Executed, ProposalCreated, Blocked, NotSupported, or a terminal autonomy state such as Failed, RolledBack, Indeterminate, or Canceled.
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("proposalId")]
    public string? ProposalId { get; set; }

    [JsonPropertyName("executionOperationId")]
    public string? ExecutionOperationId { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(OpsFindingsListResponse))]
[JsonSerializable(typeof(OpsFindingProposeResponse))]
public sealed partial class OpsFindingsJsonContext : JsonSerializerContext
{
}
