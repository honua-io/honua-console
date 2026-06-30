namespace Honua.Console.Shell.Models;

/// <summary>
/// Console-side projection of the honua-server operation-proposal kind
/// (<c>OperationClass</c>, honua-server #1690/#1694). These are the mutating
/// control-plane operation classes subject to the edition guardrail ladder. The
/// console mirrors the server enum 1:1 and adds <see cref="Unknown"/> for a value
/// it does not recognize (never guessed) and <see cref="DataImport"/> for the
/// data-import GP-job proposal (honua-server #1630) the founder's reference card
/// targets — the server emits the wire kind string and the console parses it.
/// </summary>
public enum ConsoleProposalKind
{
    /// <summary>The connected server reported a kind Console does not recognize.</summary>
    Unknown,

    /// <summary>Administrative configuration change (settings, RBAC, identity, service/layer config).</summary>
    AdminConfigChange,

    /// <summary>Deploy or rollback of a target revision through the control plane.</summary>
    Deploy,

    /// <summary>Metadata release package progression (deploy/evidence/rollback).</summary>
    MetadataRelease,

    /// <summary>Seed or bootstrap operation that materializes catalog/sample state.</summary>
    Seed,

    /// <summary>Data-import (ImportDataset GP job) surfaced as a proposal (honua-server #1630).</summary>
    DataImport,
}

/// <summary>
/// Console-side projection of the honua-server proposal lifecycle status
/// (<c>OperationProposalStatus</c>). Mirrors the server enum and adds
/// <see cref="Unknown"/> for an unrecognized value.
/// </summary>
public enum ConsoleProposalStatus
{
    /// <summary>The connected server reported a status Console does not recognize.</summary>
    Unknown,

    /// <summary>Planned but not yet routed for approval.</summary>
    Planned,

    /// <summary>Waiting for a human approve/reject decision (the actionable state).</summary>
    AwaitingApproval,

    /// <summary>Approved and submitted to the underlying execution pipeline.</summary>
    Submitted,

    /// <summary>The underlying operation is reconciling toward the desired state.</summary>
    Reconciling,

    /// <summary>The underlying operation completed successfully.</summary>
    Succeeded,

    /// <summary>The underlying operation failed.</summary>
    Failed,

    /// <summary>Rejected by a human approver.</summary>
    Rejected,

    /// <summary>The applied operation was rolled back.</summary>
    RolledBack,
}

/// <summary>
/// Console-side projection of the proposal risk level (<c>ProposalRiskLevel</c>).
/// </summary>
public enum ConsoleProposalRisk
{
    /// <summary>The connected server reported a risk level Console does not recognize.</summary>
    Unknown,

    /// <summary>Low-risk change.</summary>
    Low,

    /// <summary>Medium-risk change.</summary>
    Medium,

    /// <summary>High-risk change.</summary>
    High,
}

/// <summary>
/// Summary view of an operation proposal for the approval-inbox list surface
/// (honua-server <c>GET /api/v1/admin/proposals</c>, #1694). Console never
/// synthesizes a proposal (Console Patterns Charter section 11); every field is a
/// projection of the server-owned proposal record.
/// </summary>
public sealed record ConsoleProposalSummary(
    string ProposalId,
    ConsoleProposalKind Kind,
    ConsoleProposalStatus Status,
    string? RequestedBy,
    string? RequestedByAgent,
    string Summary,
    ConsoleProposalRisk RiskLevel,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    /// <summary>Whether an operator can act on this proposal right now (approve / reject).</summary>
    public bool IsAwaitingApproval => Status == ConsoleProposalStatus.AwaitingApproval;

    /// <summary>Whether the proposal has reached a terminal state (no further decision possible).</summary>
    public bool IsTerminal => Status
        is ConsoleProposalStatus.Succeeded
        or ConsoleProposalStatus.Failed
        or ConsoleProposalStatus.Rejected
        or ConsoleProposalStatus.RolledBack;
}

/// <summary>
/// Full detail view of an operation proposal including the plan/diff/dry-run/risk
/// artifacts and the resolution metadata (honua-server
/// <c>GET /api/v1/admin/proposals/{id}</c>, #1694). The same artifacts the
/// direct-execute path would have produced, surfaced for review-before-authorize.
/// </summary>
public sealed record ConsoleProposalDetail(
    string ProposalId,
    ConsoleProposalKind Kind,
    ConsoleProposalStatus Status,
    string? RequestedBy,
    string? RequestedByAgent,
    string Summary,
    IReadOnlyList<string> Diff,
    IReadOnlyList<string> DryRun,
    ConsoleProposalRisk RiskLevel,
    IReadOnlyList<string> BlockingReasons,
    IReadOnlyList<string> Warnings,
    string? GuardrailTier,
    string? ResolvedBy,
    string? ResolutionReason,
    string? ExecutionOperationId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ResolvedAt)
{
    /// <summary>Whether an operator can act on this proposal right now (approve / reject).</summary>
    public bool IsAwaitingApproval => Status == ConsoleProposalStatus.AwaitingApproval;

    /// <summary>Whether the proposal has reached a terminal state.</summary>
    public bool IsTerminal => Status
        is ConsoleProposalStatus.Succeeded
        or ConsoleProposalStatus.Failed
        or ConsoleProposalStatus.Rejected
        or ConsoleProposalStatus.RolledBack;

    /// <summary>The summary projection of this detail (for queue rows).</summary>
    public ConsoleProposalSummary ToSummary() => new(
        ProposalId,
        Kind,
        Status,
        RequestedBy,
        RequestedByAgent,
        Summary,
        RiskLevel,
        CreatedAt,
        UpdatedAt);
}

/// <summary>
/// The realtime proposal event the console receives over the honua-server admin
/// SignalR hub (<c>ProposalPending</c> / <c>ProposalResolved</c>, honua-server #1695).
/// Carries only the fields the inbox needs to react (id, kind, status, requester,
/// risk) — never the execution payload.
/// </summary>
public enum ConsoleProposalEventKind
{
    /// <summary>A new proposal is pending a human approval decision.</summary>
    Pending,

    /// <summary>A proposal was resolved (approved / rejected / reached a terminal state).</summary>
    Resolved,
}

/// <summary>A single live proposal event projected from the admin hub payload.</summary>
public sealed record ConsoleProposalEvent(
    ConsoleProposalEventKind EventKind,
    string ProposalId,
    ConsoleProposalKind Kind,
    ConsoleProposalStatus Status,
    string? RequestedBy,
    ConsoleProposalRisk RiskLevel,
    DateTimeOffset GeneratedAt);

/// <summary>
/// Parsers and presentation helpers for the console proposal projection so wire
/// strings are mapped in exactly one place and markup renders neutral, design-aligned
/// labels and status classes without re-deriving them inline. Mappings are 1:1 with the
/// server enums; an unreadable/unrecognized value maps to the Unknown member rather than
/// guessing.
/// </summary>
public static class ConsoleProposalPresentation
{
    /// <summary>Maps the wire kind string (any casing) onto <see cref="ConsoleProposalKind"/>.</summary>
    public static ConsoleProposalKind MapKind(string? raw) => Normalize(raw) switch
    {
        "adminconfigchange" => ConsoleProposalKind.AdminConfigChange,
        "deploy" => ConsoleProposalKind.Deploy,
        "metadatarelease" => ConsoleProposalKind.MetadataRelease,
        "seed" => ConsoleProposalKind.Seed,
        // The data-import GP job (honua-server #1630) may surface under any of these
        // server-emitted kind strings; the console parses the wire value (never prose).
        "dataimport" or "importdataset" or "import" => ConsoleProposalKind.DataImport,
        _ => ConsoleProposalKind.Unknown,
    };

    /// <summary>Maps the wire status string (any casing) onto <see cref="ConsoleProposalStatus"/>.</summary>
    public static ConsoleProposalStatus MapStatus(string? raw) => Normalize(raw) switch
    {
        "planned" => ConsoleProposalStatus.Planned,
        "awaitingapproval" => ConsoleProposalStatus.AwaitingApproval,
        "submitted" => ConsoleProposalStatus.Submitted,
        "reconciling" => ConsoleProposalStatus.Reconciling,
        "succeeded" => ConsoleProposalStatus.Succeeded,
        "failed" => ConsoleProposalStatus.Failed,
        "rejected" => ConsoleProposalStatus.Rejected,
        "rolledback" => ConsoleProposalStatus.RolledBack,
        _ => ConsoleProposalStatus.Unknown,
    };

    /// <summary>Maps the wire risk string (any casing) onto <see cref="ConsoleProposalRisk"/>.</summary>
    public static ConsoleProposalRisk MapRisk(string? raw) => Normalize(raw) switch
    {
        "low" => ConsoleProposalRisk.Low,
        "medium" => ConsoleProposalRisk.Medium,
        "high" => ConsoleProposalRisk.High,
        _ => ConsoleProposalRisk.Unknown,
    };

    /// <summary>Short, human label for a proposal kind.</summary>
    public static string KindLabel(ConsoleProposalKind kind) => kind switch
    {
        ConsoleProposalKind.AdminConfigChange => "Admin config change",
        ConsoleProposalKind.Deploy => "Deploy",
        ConsoleProposalKind.MetadataRelease => "Metadata release",
        ConsoleProposalKind.Seed => "Seed",
        ConsoleProposalKind.DataImport => "Data import",
        _ => "Unknown",
    };

    /// <summary>Short, human label for a proposal status.</summary>
    public static string StatusLabel(ConsoleProposalStatus status) => status switch
    {
        ConsoleProposalStatus.Planned => "planned",
        ConsoleProposalStatus.AwaitingApproval => "awaiting approval",
        ConsoleProposalStatus.Submitted => "submitted",
        ConsoleProposalStatus.Reconciling => "reconciling",
        ConsoleProposalStatus.Succeeded => "succeeded",
        ConsoleProposalStatus.Failed => "failed",
        ConsoleProposalStatus.Rejected => "rejected",
        ConsoleProposalStatus.RolledBack => "rolled back",
        _ => "unknown",
    };

    /// <summary>Neutral Console status CSS class for a proposal lifecycle state.</summary>
    public static string StatusClass(ConsoleProposalStatus status) => status switch
    {
        ConsoleProposalStatus.Succeeded => "console-state-success",
        ConsoleProposalStatus.Failed => "console-state-danger",
        ConsoleProposalStatus.Rejected => "console-state-danger",
        ConsoleProposalStatus.AwaitingApproval => "console-state-warning",
        ConsoleProposalStatus.RolledBack => "console-state-warning",
        ConsoleProposalStatus.Submitted or ConsoleProposalStatus.Reconciling => "console-state-info",
        _ => "console-state-neutral",
    };

    /// <summary>Short, human label for a risk level.</summary>
    public static string RiskLabel(ConsoleProposalRisk risk) => risk switch
    {
        ConsoleProposalRisk.Low => "low risk",
        ConsoleProposalRisk.Medium => "medium risk",
        ConsoleProposalRisk.High => "high risk",
        _ => "risk unknown",
    };

    /// <summary>Neutral Console status CSS class for a risk level.</summary>
    public static string RiskClass(ConsoleProposalRisk risk) => risk switch
    {
        ConsoleProposalRisk.High => "console-state-danger",
        ConsoleProposalRisk.Medium => "console-state-warning",
        ConsoleProposalRisk.Low => "console-state-success",
        _ => "console-state-neutral",
    };

    private static string Normalize(string? raw) => (raw ?? string.Empty)
        .Trim()
        .Replace("-", string.Empty, StringComparison.Ordinal)
        .Replace("_", string.Empty, StringComparison.Ordinal)
        .ToLowerInvariant();
}
