namespace Honua.Console.Shell.Models;

internal sealed record ConsoleProposalDecisionFeedback(string Message, string CssClass)
{
    public static ConsoleProposalDecisionFeedback ForApproval(ConsoleProposalDetail proposal)
    {
        var (message, style) = proposal.Status switch
        {
            ConsoleProposalStatus.Submitted => ("Approved — proposal submitted for execution.", "warning"),
            ConsoleProposalStatus.Executing => ("Approval recorded — execution is in progress.", "warning"),
            ConsoleProposalStatus.Succeeded => ("Approval recorded — change applied.", "success"),
            ConsoleProposalStatus.Completed => ("Approval recorded — execution completed.", "success"),
            ConsoleProposalStatus.Failed => ("Approval recorded — change was not applied.", "danger"),
            ConsoleProposalStatus.RolledBack => ("Approval recorded — change was rolled back.", "danger"),
            ConsoleProposalStatus.Cancelled => ("Approval recorded — execution was cancelled.", "warning"),
            ConsoleProposalStatus.Rejected => ("Approval recorded — proposal was rejected.", "warning"),
            ConsoleProposalStatus.Indeterminate => ("Approval recorded — execution outcome is indeterminate.", "warning"),
            ConsoleProposalStatus.Reconciling => ("Approval recorded — execution is being reconciled.", "warning"),
            ConsoleProposalStatus.Planned => ("Approval recorded — execution is planned.", "warning"),
            ConsoleProposalStatus.AwaitingApproval => ("Decision returned — proposal is still awaiting approval.", "warning"),
            _ => ("Approval recorded — execution status is unknown.", "warning"),
        };
        var reason = ResolutionReason(proposal);
        return new(string.IsNullOrWhiteSpace(reason) ? message : $"{message} {reason}", $"console-state-{style}");
    }

    private static string? ResolutionReason(ConsoleProposalDetail proposal) =>
        !string.IsNullOrWhiteSpace(proposal.ResolutionReason)
            ? proposal.ResolutionReason
            : string.Join(" ", proposal.BlockingReasons.Where(reason => !string.IsNullOrWhiteSpace(reason)));
}
