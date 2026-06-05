using System.Text.Json.Serialization;

namespace Honua.Console.Contracts;

// SHIM(honua-support#20): These records mirror the honua-support ticket HTTP
// surface (POST/GET /api/v1/tickets) defined in
// Honua.Support.Api/Contracts/CreateTicketRequest.cs. honua-console takes NO
// code dependency on honua-support; this is the single Console-side contract
// boundary for the in-product support loop, exactly like the Operate
// observability shim. The shared auto-bundle telemetry contract (instance URL +
// scoped key the support service uses to pull live telemetry) is tracked
// separately in honua-support#20; until it lands the Console attaches the
// instance URL and an optional scoped key on the create request and ships the
// captured context (session, env, build, route, recent errors) as structured
// symptom context.
//
// Route map (concrete v1), all under /api/v1/tickets:
//   POST /                 -> TicketWireResponse   (create)
//   GET  /{id}             -> TicketWireResponse   (poll detail)
//
// JSON on the wire is camelCase.

public static class SupportTicketRoutes
{
    public const string Tickets = "api/v1/tickets";

    public static string Ticket(string id) => $"{Tickets}/{Uri.EscapeDataString(id)}";
}

/// <summary>
/// Mirrors honua-support <c>CreateTicketRequest</c>. The Console fills the
/// operational fields from a small form and auto-attaches captured context
/// (session, environment, build, route, recent errors) into <see cref="Symptoms"/>
/// so customers ship context, not log dumps.
/// </summary>
public sealed record CreateSupportTicketRequest
{
    public string Severity { get; init; } = "sev3";

    public string Environment { get; init; } = string.Empty;

    public string Service { get; init; } = "honua-server";

    public string Symptoms { get; init; } = string.Empty;

    public string RequestedAction { get; init; } = string.Empty;

    public string AllowedAccessMode { get; init; } = "read-only";

    public int TtlMinutes { get; init; } = 60;

    public bool RollbackExpected { get; init; }

    public string? InstanceUrl { get; init; }

    public string? CustomerId { get; init; }
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
}

/// <summary>
/// Mirrors the honua-support <c>TicketResponse</c> shape (the fields the Console
/// loop needs: phase, customer status, diagnosis with guided commands, and the
/// timestamps). Unmodelled server fields are tolerated (case-insensitive,
/// ignore-missing) so the Console keeps reading as honua-support evolves.
/// </summary>
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

    public string? ResolutionSummary { get; init; }

    public SupportTicketApproval? Approval { get; init; }

    public int PhaseHistoryCount { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }

    public DateTimeOffset? ResolvedAt { get; init; }
}

/// <summary>
/// Source-generated serialization context for the support ticket contracts.
/// Source generation keeps the surface trim/AOT-safe (no reflection-based
/// serialization), per the Console runtime constraints, mirroring
/// <see cref="OperateObservabilityJsonContext"/>.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(CreateSupportTicketRequest))]
[JsonSerializable(typeof(SupportTicketResponse))]
public sealed partial class SupportTicketJsonContext : JsonSerializerContext;
