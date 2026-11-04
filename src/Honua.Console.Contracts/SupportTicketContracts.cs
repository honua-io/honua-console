using System.Text.Json;
using System.Text.Json.Serialization;

namespace Honua.Console.Contracts;

public static class SupportTicketRoutes
{
    public const string Tickets = "api/v1/tickets";

    public static string Ticket(string id) => $"{Tickets}/{Uri.EscapeDataString(id)}";
}

public sealed record CreateSupportTicketRequest
{
    public string Severity { get; init; } = "sev3";

    public string Environment { get; init; } = string.Empty;

    public string Service { get; init; } = "honua-server";

    // Symptoms carry the user's own description, including L0 activity, not a log dump.
    public string Symptoms { get; init; } = string.Empty;

    public string RequestedAction { get; init; } = string.Empty;

    public string AllowedAccessMode { get; init; } = "read-only";

    public int TtlMinutes { get; init; } = 60;

    public bool RollbackExpected { get; init; }

    public string? InstanceUrl { get; init; }

    public string? CustomerId { get; init; }

    public SupportContextRequest? Context { get; init; }
}

public sealed record SupportContextRequest
{
    // Always "1.0" for this contract; added fields stay additive on v1.
    public string SchemaVersion { get; init; } = "1.0";

    public SupportContextUser? User { get; init; }

    public SupportContextTenant? Tenant { get; init; }

    public string? EnvKind { get; init; }

    public string? AppVersion { get; init; }

    public string? Commit { get; init; }

    public string? Route { get; init; }

    public IReadOnlyList<SupportContextError>? RecentErrors { get; init; }

    public string? InstanceUrl { get; init; }

    // Secret: a short-lived read-only telemetry key, sent on the request only and never rendered or echoed back.
    public string? ScopedKey { get; init; }
}

public sealed record SupportContextUser
{
    public string? Id { get; init; }

    public string? Email { get; init; }

    public string? DisplayName { get; init; }
}

public sealed record SupportContextTenant
{
    public string? Id { get; init; }

    public string? Name { get; init; }
}

public sealed record SupportContextError
{
    public DateTimeOffset? Timestamp { get; init; }

    public string? Message { get; init; }

    public string? CorrelationId { get; init; }

    public string? Path { get; init; }

    public int? StatusCode { get; init; }
}

public sealed record SupportTicketApproval
{
    public string ApprovedBy { get; init; } = string.Empty;

    public DateTimeOffset? ApprovedAt { get; init; }
}

public sealed record SupportTicketDiagnosis
{
    public string Summary { get; init; } = string.Empty;

    public string Confidence { get; init; } = string.Empty;

    public string Mode { get; init; } = string.Empty;

    public IReadOnlyList<string> GuidedCommands { get; init; } = [];

    public IReadOnlyList<string> ValidationSteps { get; init; } = [];

    public DateTimeOffset DiagnosedAt { get; init; }

    public SupportEscalationRationale? Escalation { get; init; }

    // Free-form diagnosis JSON. Structured per-criterion results live on the trust scorecard, not here.
    public JsonElement? Scorecard { get; init; }
}

public sealed record SupportEscalationRationale
{
    public string Justification { get; init; } = string.Empty;

    public string AccessScope { get; init; } = string.Empty;

    public int TtlMinutes { get; init; }

    public string RollbackIntent { get; init; } = string.Empty;

    public string? ApprovedBy { get; init; }

    public DateTimeOffset? ApprovedAt { get; init; }

    public string? Trigger { get; init; }

    public string? Signal { get; init; }
}

public sealed record SupportAccessBoundary
{
    public string FirstEscalationStep { get; init; } = string.Empty;

    public string OperatorScopedRequirement { get; init; } = string.Empty;

    public IReadOnlyList<string> Exclusions { get; init; } = [];
}

public sealed record SupportTicketTrust
{
    public SupportTrustDelegatedSession? DelegatedSession { get; init; }

    public SupportTrustScorecard? Scorecard { get; init; }

    public SupportTrustEscalation? Escalation { get; init; }
}

public sealed record SupportTrustDelegatedSession
{
    // One of disabled, read-only, or operator-scoped.
    public string Mode { get; init; } = string.Empty;

    public DateTimeOffset? EstablishedAt { get; init; }

    public DateTimeOffset? ExpiresAt { get; init; }

    public bool CustomerVisible { get; init; }

    public bool Active { get; init; }
}

public sealed record SupportTrustScorecard
{
    // One of pass or fail.
    public string OverallResult { get; init; } = string.Empty;

    public double? Score { get; init; }

    // One of low, medium, or high.
    public string? Confidence { get; init; }

    public IReadOnlyList<SupportTrustCriterion> Criteria { get; init; } = [];

    public IReadOnlyList<string> FailureModes { get; init; } = [];

    public IReadOnlyList<string> EvidenceRefs { get; init; } = [];
}

public sealed record SupportTrustCriterion
{
    public string Name { get; init; } = string.Empty;

    public bool Passed { get; init; }
}

public sealed record SupportTrustEscalation
{
    public bool Escalated { get; init; }

    public string? Trigger { get; init; }

    public string? Signal { get; init; }

    public string? Justification { get; init; }

    public string? AccessScope { get; init; }

    public int? TtlMinutes { get; init; }

    public string? RollbackIntent { get; init; }

    public string? ApprovedBy { get; init; }

    public DateTimeOffset? ApprovedAt { get; init; }
}

public sealed record SupportTicketResponse
{
    public string Id { get; init; } = string.Empty;

    public string CustomerId { get; init; } = string.Empty;

    public string Severity { get; init; } = string.Empty;

    public string Environment { get; init; } = string.Empty;

    public string Service { get; init; } = string.Empty;

    public string Symptoms { get; init; } = string.Empty;

    public string RequestedAction { get; init; } = string.Empty;

    public string AllowedAccessMode { get; init; } = string.Empty;

    public int TtlMinutes { get; init; }

    public bool RollbackExpected { get; init; }

    public string? InstanceUrl { get; init; }

    public string Phase { get; init; } = string.Empty;

    public string CustomerStatus { get; init; } = string.Empty;

    public SupportTicketDiagnosis? Diagnosis { get; init; }

    public int AttachmentCount { get; init; }

    public string? AssignedOperatorId { get; init; }

    public int? EscalationTier { get; init; }

    public DateTimeOffset? EscalatedAt { get; init; }

    public string? EscalatedBy { get; init; }

    public SupportAccessBoundary? AccessBoundary { get; init; }

    // When present, this envelope supersedes the best-effort diagnosis, scorecard, and access fields.
    public SupportTicketTrust? Trust { get; init; }

    public string? ResolutionSummary { get; init; }

    public SupportTicketApproval? Approval { get; init; }

    public int PhaseHistoryCount { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }

    public DateTimeOffset? ResolvedAt { get; init; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(CreateSupportTicketRequest))]
[JsonSerializable(typeof(SupportTicketResponse))]
[JsonSerializable(typeof(SupportContextRequest))]
[JsonSerializable(typeof(SupportContextUser))]
[JsonSerializable(typeof(SupportContextTenant))]
[JsonSerializable(typeof(SupportContextError))]
[JsonSerializable(typeof(SupportEscalationRationale))]
[JsonSerializable(typeof(SupportAccessBoundary))]
[JsonSerializable(typeof(SupportTicketTrust))]
[JsonSerializable(typeof(SupportTrustDelegatedSession))]
[JsonSerializable(typeof(SupportTrustScorecard))]
[JsonSerializable(typeof(SupportTrustCriterion))]
[JsonSerializable(typeof(SupportTrustEscalation))]
public sealed partial class SupportTicketJsonContext : JsonSerializerContext;
