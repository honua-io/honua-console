namespace Honua.Console.Contracts;

public static class ProposalAdminRoutes
{
    [OpsParityRoute("GET")]
    public const string List = "api/v1/admin/proposals";

    [OpsParityRoute("GET")]
    public const string DetailTemplate = List + "/{id}";

    [OpsParityRoute("POST")]
    public const string ApproveTemplate = DetailTemplate + "/approve";

    [OpsParityRoute("GET")]
    public const string ApprovalReviewTemplate = DetailTemplate + "/approval-review";

    // Carries the short-lived capability returned by approval-review on the approve request.
    public const string ApprovalCapabilityHeader = "X-Honua-Approval-Capability";

    [OpsParityRoute("POST")]
    public const string RejectTemplate = DetailTemplate + "/reject";

    public static string Detail(string proposalId) =>
        DetailTemplate.Replace("{id}", Uri.EscapeDataString(proposalId), StringComparison.Ordinal);

    public static string Approve(string proposalId) =>
        ApproveTemplate.Replace("{id}", Uri.EscapeDataString(proposalId), StringComparison.Ordinal);

    public static string ApprovalReview(string proposalId) =>
        ApprovalReviewTemplate.Replace("{id}", Uri.EscapeDataString(proposalId), StringComparison.Ordinal);

    public static string Reject(string proposalId) =>
        RejectTemplate.Replace("{id}", Uri.EscapeDataString(proposalId), StringComparison.Ordinal);
}
