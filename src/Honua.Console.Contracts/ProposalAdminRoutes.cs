namespace Honua.Console.Contracts;

/// <summary>
/// Concrete v1 routes for the honua-server proposal review endpoints.
/// </summary>
public static class ProposalAdminRoutes
{
    /// <summary>The proposal list route.</summary>
    [OpsParityRoute("GET")]
    public const string List = "api/v1/admin/proposals";

    /// <summary>The route template for one proposal.</summary>
    [OpsParityRoute("GET")]
    public const string DetailTemplate = List + "/{id}";

    /// <summary>The route template for human approval of one proposal.</summary>
    [OpsParityRoute("POST")]
    public const string ApproveTemplate = DetailTemplate + "/approve";

    /// <summary>The route template for human rejection of one proposal.</summary>
    [OpsParityRoute("POST")]
    public const string RejectTemplate = DetailTemplate + "/reject";

    /// <summary>Builds the route for one proposal.</summary>
    public static string Detail(string proposalId) =>
        DetailTemplate.Replace("{id}", Uri.EscapeDataString(proposalId), StringComparison.Ordinal);

    /// <summary>Builds the route for approving one proposal.</summary>
    public static string Approve(string proposalId) =>
        ApproveTemplate.Replace("{id}", Uri.EscapeDataString(proposalId), StringComparison.Ordinal);

    /// <summary>Builds the route for rejecting one proposal.</summary>
    public static string Reject(string proposalId) =>
        RejectTemplate.Replace("{id}", Uri.EscapeDataString(proposalId), StringComparison.Ordinal);
}
