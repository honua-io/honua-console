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

    /// <summary>A map deliverable the agent-first console proposes for authoring/publish (honua-devops deliverable lifecycle).</summary>
    Map,

    /// <summary>An analysis deliverable proposed for authoring/publish (honua-devops deliverable lifecycle).</summary>
    Analysis,

    /// <summary>A dashboard deliverable proposed for authoring/publish (honua-devops deliverable lifecycle).</summary>
    Dashboard,

    /// <summary>An app deliverable proposed for authoring/publish (honua-devops deliverable lifecycle).</summary>
    App,
}

/// <summary>
/// The system that owns a proposal in the aggregated approval inbox (issue #193). The console
/// aggregates two sources on one surface (honua-server #1690's locked ownership split):
/// honua-server owns admin/deploy/metadata/seed proposals; honua-devops owns gitops/infra and
/// deliverable proposals via its console bridge. The source is a projection tag only — it never
/// changes the fact that the owning system is the sole safety gate for approve/reject.
/// </summary>
public enum ConsoleProposalSource
{
    /// <summary>A honua-server-owned proposal (admin config / deploy / metadata release / seed / data import).</summary>
    Server,

    /// <summary>A honua-devops-owned proposal aggregated through the console-bridge gitops/deliverable contract.</summary>
    DevOps,
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
    /// <summary>
    /// The system that owns this proposal (server vs devops-bridge). Defaults to
    /// <see cref="ConsoleProposalSource.Server"/> so server projections carry the correct tag by
    /// construction; the devops source sets <see cref="ConsoleProposalSource.DevOps"/>.
    /// </summary>
    public ConsoleProposalSource Source { get; init; } = ConsoleProposalSource.Server;

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
    /// <summary>The system that owns this proposal (server vs devops-bridge). See <see cref="ConsoleProposalSource"/>.</summary>
    public ConsoleProposalSource Source { get; init; } = ConsoleProposalSource.Server;

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
        UpdatedAt)
    {
        Source = Source,
    };
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
        // Deliverable-request kinds the agent-first console proposes (map / analysis /
        // dashboard / app). These arrive from the honua-devops deliverable lifecycle bridge
        // (and any server-emitted deliverable proposal); the console parses the wire value.
        "map" => ConsoleProposalKind.Map,
        "analysis" => ConsoleProposalKind.Analysis,
        "dashboard" => ConsoleProposalKind.Dashboard,
        "app" or "application" => ConsoleProposalKind.App,
        // The honua-devops gitops proposal bridge emits kind "gitops-deploy"; it is a deploy
        // of a target revision through the control plane, so it maps onto the Deploy kind (the
        // ConsoleProposalSource.DevOps tag carries the gitops/infra provenance).
        "gitopsdeploy" or "gitops" => ConsoleProposalKind.Deploy,
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
        ConsoleProposalKind.Map => "Map",
        ConsoleProposalKind.Analysis => "Analysis",
        ConsoleProposalKind.Dashboard => "Dashboard",
        ConsoleProposalKind.App => "App",
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

    /// <summary>
    /// Neutral Console status CSS class for a proposal lifecycle state, derived from the shared
    /// <see cref="OperateStatus"/> mapping table (console#293) so this stays identical to the
    /// class every other Operate surface would render for the same word.
    /// </summary>
    public static string StatusClass(ConsoleProposalStatus status) => ToStatus(status).CssClass;

    /// <summary>
    /// Projects a proposal lifecycle status onto the shared <see cref="OperateStatus"/> status
    /// vocabulary (console#293) so it can render through the shared <c>OperateStatusPill</c>
    /// component.
    /// </summary>
    public static OperateStatus ToStatus(ConsoleProposalStatus status) =>
        new(StatusLabel(status), string.Empty);

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

    /// <summary>
    /// Whether a kind is a deliverable-request (map / analysis / dashboard / app) — the
    /// artifacts the agent-first console proposes for authoring and publish. These get a
    /// first-class deliverable card treatment alongside the data-import card.
    /// </summary>
    public static bool IsDeliverable(ConsoleProposalKind kind) => kind
        is ConsoleProposalKind.Map
        or ConsoleProposalKind.Analysis
        or ConsoleProposalKind.Dashboard
        or ConsoleProposalKind.App;

    /// <summary>
    /// The card kicker (eyebrow) for the approval panel, giving each first-class kind its own
    /// treatment: the data-import card, a per-deliverable card, else the neutral review kicker.
    /// </summary>
    public static string CardKicker(ConsoleProposalKind kind) => kind switch
    {
        ConsoleProposalKind.DataImport => "Data import approval",
        ConsoleProposalKind.Map => "Map request approval",
        ConsoleProposalKind.Analysis => "Analysis request approval",
        ConsoleProposalKind.Dashboard => "Dashboard request approval",
        ConsoleProposalKind.App => "App request approval",
        _ => "Proposal review",
    };

    /// <summary>
    /// A CSS modifier class for the approval panel so each first-class kind can be styled
    /// distinctly (the existing <c>is-data-import</c> treatment plus per-deliverable variants).
    /// Returns <c>null</c> for kinds that use the neutral panel.
    /// </summary>
    public static string? CardVariantClass(ConsoleProposalKind kind) => kind switch
    {
        ConsoleProposalKind.DataImport => "is-data-import",
        ConsoleProposalKind.Map => "is-deliverable is-deliverable-map",
        ConsoleProposalKind.Analysis => "is-deliverable is-deliverable-analysis",
        ConsoleProposalKind.Dashboard => "is-deliverable is-deliverable-dashboard",
        ConsoleProposalKind.App => "is-deliverable is-deliverable-app",
        _ => null,
    };

    /// <summary>The plan/diff section heading, made kind-appropriate for the first-class cards.</summary>
    public static string PlanDiffHeading(ConsoleProposalKind kind) => kind switch
    {
        ConsoleProposalKind.DataImport => "Import plan & diff",
        ConsoleProposalKind.Map => "Map plan & diff",
        ConsoleProposalKind.Analysis => "Analysis plan & diff",
        ConsoleProposalKind.Dashboard => "Dashboard plan & diff",
        ConsoleProposalKind.App => "App plan & diff",
        _ => "Plan & diff",
    };

    /// <summary>
    /// The dry-run section heading. For a deliverable request the dry-run IS the rendered
    /// preview of the artifact, so the section reads "Preview"; other kinds keep "Dry run".
    /// </summary>
    public static string PreviewHeading(ConsoleProposalKind kind) =>
        IsDeliverable(kind) ? "Preview" : "Dry run";

    /// <summary>Short, human label for a proposal source (server vs devops-bridge).</summary>
    public static string SourceLabel(ConsoleProposalSource source) => source switch
    {
        ConsoleProposalSource.DevOps => "DevOps",
        _ => "Server",
    };

    private static string Normalize(string? raw) => (raw ?? string.Empty)
        .Trim()
        .Replace("-", string.Empty, StringComparison.Ordinal)
        .Replace("_", string.Empty, StringComparison.Ordinal)
        .ToLowerInvariant();
}
