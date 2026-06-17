namespace Honua.Console.Shell.Models;

/// <summary>
/// The honua-server deploy-control operation lifecycle, mapped onto a neutral
/// Console vocabulary. Mirrors the server <c>WorkflowOperationStatus</c> 1:1
/// (<c>Planned -> AwaitingApproval -> Submitted -> Reconciling -> Succeeded /
/// Failed -> RolledBack</c>) plus a request/rollback-requested intermediate and
/// an explicit unknown for a status the connected server reports that Console
/// does not recognize. Drives the human-in-the-loop approval surface.
/// </summary>
public enum DeployOperationLifecycle
{
    /// <summary>The connected server reported a status Console does not recognize.</summary>
    Unknown,

    /// <summary>Created but not yet awaiting approval (lifecycle entry state).</summary>
    Planned,

    /// <summary>Paused pending an operator approval decision (the actionable state).</summary>
    AwaitingApproval,

    /// <summary>Approved and submitted for execution.</summary>
    Submitted,

    /// <summary>Submitted and reconciling toward the desired revision.</summary>
    Reconciling,

    /// <summary>Reached the desired revision successfully.</summary>
    Succeeded,

    /// <summary>Failed to reach the desired revision.</summary>
    Failed,

    /// <summary>A rollback has been requested and is in progress.</summary>
    RollbackRequested,

    /// <summary>Rolled back to the prior revision.</summary>
    RolledBack,

    /// <summary>The server requires manual intervention before proceeding.</summary>
    ManualInterventionRequired,
}

/// <summary>
/// One pending/active deploy-control operation as projected for the approval
/// surface: the durable server operation id, lifecycle status, the
/// service/environment/revision/action being proposed, a change summary, the
/// evidence links, the requested approval reason, and the suggested rollback
/// plan. This is a server-owned projection over the deploy-control operation
/// record; Console never synthesizes it (Charter section 11).
/// </summary>
public sealed record DeployOperationProposal(
    string OperationId,
    DeployOperationLifecycle Lifecycle,
    string RawStatus,
    string Kind,
    string Priority,
    string Service,
    string Environment,
    string DesiredRevision,
    string? CurrentRevision,
    string Action,
    string ChangeSummary,
    string? RequestedBy,
    string? Reason,
    string? PrUrl,
    string? CommitSha,
    IReadOnlyList<DeployOperationEvidenceLink> Evidence,
    DeployOperationRollbackSummary? RollbackPlan,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> BlockingReasons,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    /// <summary>
    /// Whether the operation is in the actionable approval state: an operator
    /// can approve (submit) or reject it. Only <see cref="DeployOperationLifecycle.AwaitingApproval"/>
    /// is approvable.
    /// </summary>
    public bool IsAwaitingApproval => Lifecycle == DeployOperationLifecycle.AwaitingApproval;

    /// <summary>
    /// Whether a rollback can be offered: the operation has been submitted or has
    /// reached a state where rolling back to the prior revision is meaningful, and a
    /// rollback is not already in progress / completed.
    /// </summary>
    public bool IsRollbackEligible => Lifecycle is
        DeployOperationLifecycle.Submitted
        or DeployOperationLifecycle.Reconciling
        or DeployOperationLifecycle.Succeeded
        or DeployOperationLifecycle.Failed
        or DeployOperationLifecycle.ManualInterventionRequired;

    /// <summary>
    /// Whether the lifecycle is terminal (no further operator action changes it).
    /// </summary>
    public bool IsTerminal => Lifecycle is
        DeployOperationLifecycle.Succeeded
        or DeployOperationLifecycle.Failed
        or DeployOperationLifecycle.RolledBack;
}

/// <summary>
/// An evidence reference attached to a deploy-control operation (preflight, plan,
/// PR, CI, etc.), surfaced as a link the operator can open before approving.
/// </summary>
public sealed record DeployOperationEvidenceLink(
    string Kind,
    string RefId,
    string? Uri);

/// <summary>
/// The suggested rollback plan for a deploy-control operation, including whether it
/// is data-affecting and therefore requires explicit confirmation/approval. The
/// approval surface MUST require explicit confirmation before invoking a
/// data-affecting rollback (the server's OperatorApprovalGate enforces it server-side;
/// the UI must not weaken it).
/// </summary>
public sealed record DeployOperationRollbackSummary(
    GitOpsRollbackClassification Classification,
    bool IsDataAffecting,
    bool RequiresExplicitApproval,
    IReadOnlyList<string> Steps,
    IReadOnlyList<string> EvidenceRequired,
    string? ApprovalPolicyRef)
{
    /// <summary>
    /// Whether the operator must explicitly confirm before this rollback runs. A
    /// data-affecting rollback always requires confirmation; the server gate is the
    /// authority, but the UI surfaces the requirement up-front.
    /// </summary>
    public bool RequiresConfirmation => IsDataAffecting || RequiresExplicitApproval;
}

/// <summary>
/// Presentation helpers for the deploy-control approval surface so the components
/// render neutral, design-aligned lifecycle labels and status classes without
/// re-deriving semantics from prose.
/// </summary>
public static class DeployOperationPresentation
{
    public static string Label(DeployOperationLifecycle lifecycle) => lifecycle switch
    {
        DeployOperationLifecycle.Planned => "planned",
        DeployOperationLifecycle.AwaitingApproval => "awaiting approval",
        DeployOperationLifecycle.Submitted => "submitted",
        DeployOperationLifecycle.Reconciling => "reconciling",
        DeployOperationLifecycle.Succeeded => "succeeded",
        DeployOperationLifecycle.Failed => "failed",
        DeployOperationLifecycle.RollbackRequested => "rollback requested",
        DeployOperationLifecycle.RolledBack => "rolled back",
        DeployOperationLifecycle.ManualInterventionRequired => "manual intervention required",
        _ => "unknown",
    };

    /// <summary>Neutral Console status CSS class for a lifecycle state.</summary>
    public static string StateClass(DeployOperationLifecycle lifecycle) => lifecycle switch
    {
        DeployOperationLifecycle.Succeeded => "console-state-success",
        DeployOperationLifecycle.Failed or DeployOperationLifecycle.ManualInterventionRequired => "console-state-danger",
        DeployOperationLifecycle.AwaitingApproval => "console-state-warning",
        DeployOperationLifecycle.RolledBack or DeployOperationLifecycle.RollbackRequested => "console-state-warning",
        DeployOperationLifecycle.Submitted or DeployOperationLifecycle.Reconciling => "console-state-info",
        _ => "console-state-neutral",
    };

    /// <summary>
    /// Maps a honua-server WorkflowOperationStatus (any casing, hyphenated or
    /// PascalCase) onto the canonical <see cref="DeployOperationLifecycle"/>. The
    /// mapping is 1:1 with the server enum; an unreadable/unrecognized value maps to
    /// <see cref="DeployOperationLifecycle.Unknown"/> rather than guessing.
    /// </summary>
    public static DeployOperationLifecycle MapLifecycle(string? rawStatus)
    {
        var normalized = (rawStatus ?? string.Empty)
            .Trim()
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();

        return normalized switch
        {
            "planned" => DeployOperationLifecycle.Planned,
            "awaitingapproval" => DeployOperationLifecycle.AwaitingApproval,
            "submitted" => DeployOperationLifecycle.Submitted,
            "reconciling" => DeployOperationLifecycle.Reconciling,
            "succeeded" => DeployOperationLifecycle.Succeeded,
            "failed" => DeployOperationLifecycle.Failed,
            "rollbackrequested" => DeployOperationLifecycle.RollbackRequested,
            "rolledback" => DeployOperationLifecycle.RolledBack,
            "manualinterventionrequired" => DeployOperationLifecycle.ManualInterventionRequired,
            _ => DeployOperationLifecycle.Unknown,
        };
    }
}
