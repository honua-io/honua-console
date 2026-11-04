namespace Honua.Console.Shell.Models;

public enum DeployOperationLifecycle
{
    Unknown,

    Planned,

    AwaitingApproval,

    Submitted,

    Reconciling,

    Succeeded,

    Failed,

    RollbackRequested,

    RolledBack,

    ManualInterventionRequired,
}

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
    public bool IsAwaitingApproval => Lifecycle == DeployOperationLifecycle.AwaitingApproval;

    public bool IsRollbackEligible => Lifecycle is
        DeployOperationLifecycle.Submitted
        or DeployOperationLifecycle.Reconciling
        or DeployOperationLifecycle.Succeeded
        or DeployOperationLifecycle.Failed
        or DeployOperationLifecycle.ManualInterventionRequired;

    public bool IsTerminal => Lifecycle is
        DeployOperationLifecycle.Succeeded
        or DeployOperationLifecycle.Failed
        or DeployOperationLifecycle.RolledBack;

    public bool IsServerUpgrade =>
        string.Equals(Kind, "Deploy", StringComparison.OrdinalIgnoreCase)
        || string.Equals(Kind, "Rollback", StringComparison.OrdinalIgnoreCase);

    public bool IsMetadataPromotion =>
        string.Equals(Kind, "MetadataRelease", StringComparison.OrdinalIgnoreCase);
}

public sealed record DeployOperationEvidenceLink(
    string Kind,
    string RefId,
    string? Uri);

// A data-affecting rollback needs explicit confirmation; the server gate is the authority and the UI must not weaken it.
public sealed record DeployOperationRollbackSummary(
    GitOpsRollbackClassification Classification,
    bool IsDataAffecting,
    bool RequiresExplicitApproval,
    IReadOnlyList<string> Steps,
    IReadOnlyList<string> EvidenceRequired,
    string? ApprovalPolicyRef)
{
    public bool RequiresConfirmation => IsDataAffecting || RequiresExplicitApproval;
}

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

    public static string StateClass(DeployOperationLifecycle lifecycle) => ToStatus(lifecycle).CssClass;

    public static OperateStatus ToStatus(DeployOperationLifecycle lifecycle) =>
        new(Label(lifecycle), string.Empty);

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
