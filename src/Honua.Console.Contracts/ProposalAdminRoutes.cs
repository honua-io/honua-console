namespace Honua.Console.Contracts;

public static class ProposalAdminRoutes
{
    [OpsParityRoute("GET")]
    public const string List = "api/v1/admin/proposals";

    [OpsParityRoute("GET")]
    public const string DetailTemplate = List + "/{id}";

    [OpsParityRoute("GET")]
    public const string ReviewTemplate = DetailTemplate + "/approval-review";

    [OpsParityRoute("POST")]
    public const string ApproveTemplate = DetailTemplate + "/approve";

    [OpsParityRoute("POST")]
    public const string RejectTemplate = DetailTemplate + "/reject";

    public static string Detail(string proposalId) =>
        DetailTemplate.Replace("{id}", Uri.EscapeDataString(proposalId), StringComparison.Ordinal);

    public static string Approve(string proposalId) =>
        ApproveTemplate.Replace("{id}", Uri.EscapeDataString(proposalId), StringComparison.Ordinal);

    public static string Review(string proposalId) =>
        ReviewTemplate.Replace("{id}", Uri.EscapeDataString(proposalId), StringComparison.Ordinal);

    public static string Reject(string proposalId) =>
        RejectTemplate.Replace("{id}", Uri.EscapeDataString(proposalId), StringComparison.Ordinal);
}
