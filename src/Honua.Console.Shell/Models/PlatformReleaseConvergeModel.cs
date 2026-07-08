using Honua.Console.Contracts;

namespace Honua.Console.Shell.Models;

/// <summary>
/// Platform-release converge outcome view (console#290 acceptance criterion 5). Speculative:
/// honua-server#2564 (the converge API) is still open, so this always renders through the
/// capability-detected unavailable state against every server available today (a 404/501
/// response maps to <see cref="OperateSectionStatus.Unsupported"/>). Reconcile against the
/// real contract once #2564 merges — see the remarks on
/// <see cref="PlatformReleaseConvergeResponse"/> in Honua.Console.Contracts.
/// </summary>
public sealed record PlatformReleaseConvergeTargetOutcomeView(
    string TargetId,
    OperateStatus Outcome,
    string? Message,
    string? OperationId);

public sealed record PlatformReleaseConvergeView(
    IReadOnlyList<PlatformReleaseConvergeTargetOutcomeView> Targets,
    string? ProposalId,
    string? Message);

/// <summary>Maps the speculative converge response onto the Console view.</summary>
public static class PlatformReleaseConvergeMapper
{
    public static PlatformReleaseConvergeView Map(PlatformReleaseConvergeResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        return new PlatformReleaseConvergeView(
            response.Targets.Select(t => new PlatformReleaseConvergeTargetOutcomeView(
                t.TargetId,
                OutcomeStatus(t.Outcome),
                t.Message,
                t.OperationId)).ToArray(),
            response.ProposalId,
            response.Message);
    }

    private static OperateStatus OutcomeStatus(string outcome) =>
        (outcome?.Trim().ToLowerInvariant()) switch
        {
            "converged" or "succeeded" or "success" => new OperateStatus("healthy", outcome!),
            "blocked" or "requiresapproval" or "awaitingapproval" => new OperateStatus("warning", outcome!),
            "failed" or "error" => new OperateStatus("critical", outcome!),
            null or "" => new OperateStatus("unknown", "Outcome not reported."),
            _ => new OperateStatus("info", outcome!),
        };
}
