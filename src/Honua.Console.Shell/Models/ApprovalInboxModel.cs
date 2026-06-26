namespace Honua.Console.Shell.Models;

/// <summary>
/// The canonical GIS-department work-queue ticket types (founder direction 2026-06-12,
/// issue #193). The console's organizing principle is the department's work queue, not
/// system objects: each pending agent-proposed operation is classified into one of these
/// ticket types so the approval inbox can be filtered the way the GIS desk reasons about
/// requests. The classification is derived from server-owned proposal fields (never
/// guessed about the request's intent) and falls back to <see cref="Other"/> when the
/// proposal does not map onto a known ticket type.
/// </summary>
public enum ApprovalTicketType
{
    /// <summary>A change Console does not yet map onto a known GIS-desk ticket type.</summary>
    Other,

    /// <summary>Publish / update data — a metadata + service promotion across environments.</summary>
    PublishData,

    /// <summary>A platform/server version upgrade (or its rollback).</summary>
    ServerUpgrade,
}

/// <summary>
/// Presentation helpers for the approval-inbox ticket taxonomy so the inbox renders
/// neutral, design-aligned labels and filter chips without re-deriving the taxonomy
/// from prose in markup.
/// </summary>
public static class ApprovalTicketPresentation
{
    /// <summary>Short, human filter/label text for a ticket type.</summary>
    public static string Label(ApprovalTicketType type) => type switch
    {
        ApprovalTicketType.PublishData => "Publish / update data",
        ApprovalTicketType.ServerUpgrade => "Server upgrade",
        _ => "Other",
    };

    /// <summary>A one-line description of what the ticket type covers, for the filter rail.</summary>
    public static string Description(ApprovalTicketType type) => type switch
    {
        ApprovalTicketType.PublishData =>
            "Promote metadata, services, and layers across environments as a governed, rollback-able release.",
        ApprovalTicketType.ServerUpgrade =>
            "Upgrade (or roll back) the target Honua server version through a governed deploy-control operation.",
        _ => "Agent-proposed operations that do not map onto a known GIS-desk ticket type.",
    };

    /// <summary>
    /// Classifies a deploy-control proposal onto a GIS-desk ticket type using the
    /// server-owned operation kind. Mirrors the Operate Deploy page's
    /// MetadataRelease/Deploy-Rollback split; never infers intent from prose.
    /// </summary>
    public static ApprovalTicketType Classify(DeployOperationProposal proposal)
    {
        if (proposal.IsMetadataPromotion)
        {
            return ApprovalTicketType.PublishData;
        }

        if (proposal.IsServerUpgrade)
        {
            return ApprovalTicketType.ServerUpgrade;
        }

        return ApprovalTicketType.Other;
    }
}

/// <summary>
/// One item in the approval inbox: a single pending/active agent-proposed operation
/// projected onto the GIS-desk work queue, carrying its ticket-type classification and
/// the underlying server-owned <see cref="DeployOperationProposal"/>. Console never
/// synthesizes the proposal (Console Patterns Charter section 11); the inbox is a pure
/// projection over the deploy-control / GitOps-release contracts.
/// </summary>
public sealed record ApprovalInboxItem(
    ApprovalTicketType TicketType,
    DeployOperationProposal Proposal)
{
    /// <summary>The durable server operation id for this work item.</summary>
    public string OperationId => Proposal.OperationId;

    /// <summary>Whether an operator can act on this item right now (approve / reject).</summary>
    public bool IsAwaitingApproval => Proposal.IsAwaitingApproval;
}

/// <summary>
/// The aggregated approval-inbox snapshot: every pending/active proposal across the
/// proposal sources Console aggregates (deploy-control operations derived from GitOps
/// release operations, plus any operator-tracked operation ids), classified by ticket
/// type. The snapshot is ordered awaiting-approval-first so the actionable work surfaces
/// at the top of the queue.
/// </summary>
public sealed record ApprovalInboxSnapshot(IReadOnlyList<ApprovalInboxItem> Items)
{
    /// <summary>An empty snapshot (an allowed read that found no pending work).</summary>
    public static ApprovalInboxSnapshot Empty { get; } = new([]);

    /// <summary>How many items are in the actionable awaiting-approval state.</summary>
    public int AwaitingApprovalCount => Items.Count(item => item.IsAwaitingApproval);

    /// <summary>The total number of pending/active work items.</summary>
    public int TotalCount => Items.Count;

    /// <summary>The distinct ticket types present in the snapshot, in taxonomy order.</summary>
    public IReadOnlyList<ApprovalTicketType> PresentTicketTypes =>
        Items.Select(item => item.TicketType).Distinct().OrderBy(type => (int)type).ToArray();

    /// <summary>The items for one ticket type (used by the filter rail).</summary>
    public IReadOnlyList<ApprovalInboxItem> ForTicketType(ApprovalTicketType type) =>
        Items.Where(item => item.TicketType == type).ToArray();
}
