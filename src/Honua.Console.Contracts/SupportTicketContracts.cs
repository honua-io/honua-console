using System.Text.Json;
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

    // ---- trust-visibility (#166) additions ----------------------------------
    // The escalation rationale and the diagnosis scorecard travel with the
    // honua-support ticket itself (TicketDiagnosis.Escalation / .Scorecard in
    // Honua.Support.Api). These mirror those fields so the detail page can show
    // *why* escalation happened and the pass/fail scorecard. honua-devops now
    // also posts the escalation trigger/signal back onto this object; those
    // codes are modelled on SupportEscalationRationale below and populate from
    // the wire when present.
    public SupportEscalationRationale? Escalation { get; init; }

    /// <summary>
    /// Free-form diagnosis scorecard exactly as honua-support emits it
    /// (<c>TicketDiagnosis.Scorecard</c>, a raw JSON element). The richer
    /// per-criterion booleans / failure-modes from the honua-devops
    /// <c>support-ticket-view</c> ConsoleBridge projection are NOT reachable
    /// today (no Console transport to that projection exists); the page renders
    /// whatever pass/fail + criteria this object carries.
    /// </summary>
    public JsonElement? Scorecard { get; init; }
    // -------------------------------------------------------------------------
}

/// <summary>
/// trust-visibility (#166): mirrors honua-support <c>DiagnosisEscalation</c>
/// (the escalation rationale that travels on the ticket diagnosis). The
/// <see cref="Trigger"/>/<see cref="Signal"/> fields are populated by
/// honua-devops posting back onto the diagnosis escalation object; they are
/// optional and degrade to the human-readable <see cref="Justification"/> when
/// absent. Unmodelled wire fields are tolerated (ignore-missing).
/// </summary>
public sealed record SupportEscalationRationale
{
    public string Justification { get; init; } = string.Empty;

    public string AccessScope { get; init; } = string.Empty;

    public int TtlMinutes { get; init; }

    public string RollbackIntent { get; init; } = string.Empty;

    public string? ApprovedBy { get; init; }

    public DateTimeOffset? ApprovedAt { get; init; }

    /// <summary>Machine trigger code honua-devops attaches (e.g. <c>error-rate-not-cleared</c>). Optional.</summary>
    public string? Trigger { get; init; }

    /// <summary>The signal that fired the trigger (e.g. <c>http_5xx_rate</c>). Optional.</summary>
    public string? Signal { get; init; }
}

/// <summary>
/// trust-visibility (#166): mirrors honua-support
/// <c>SupportAccessBoundarySnapshot</c> — the access boundary that bounds any
/// remote session for the ticket. Combined with the ticket's
/// <c>AllowedAccessMode</c> / <c>TtlMinutes</c> this is the live remote-session
/// state reachable today. The richer live-session fields (a concrete session
/// expiry timestamp, customer-visible flag, audit/evidence refs) live only on
/// the honua-devops <c>support-ticket-view</c> ConsoleBridge projection, for
/// which no Console transport exists yet (follow-up).
/// </summary>
public sealed record SupportAccessBoundary
{
    public string FirstEscalationStep { get; init; } = string.Empty;

    public string OperatorScopedRequirement { get; init; } = string.Empty;

    public IReadOnlyList<string> Exclusions { get; init; } = [];
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

    // ---- trust-visibility (#166) additions ----------------------------------
    // Escalation provenance + the access boundary, straight off the honua-support
    // GET /tickets/{id} TicketResponse (EscalatedAt/EscalatedBy/AccessBoundary).
    public DateTimeOffset? EscalatedAt { get; init; }

    public string? EscalatedBy { get; init; }

    public SupportAccessBoundary? AccessBoundary { get; init; }
    // -------------------------------------------------------------------------

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
// trust-visibility (#166): escalation rationale + access boundary nested shapes.
[JsonSerializable(typeof(SupportEscalationRationale))]
[JsonSerializable(typeof(SupportAccessBoundary))]
public sealed partial class SupportTicketJsonContext : JsonSerializerContext;
