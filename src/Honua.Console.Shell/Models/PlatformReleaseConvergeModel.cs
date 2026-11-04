using Honua.Console.Contracts;

namespace Honua.Console.Shell.Models;

public sealed record PlatformReleaseConvergeTargetOutcomeView(
    string TargetId,
    OperateStatus Outcome,
    string? Message,
    string? OperationId);

public sealed record PlatformReleaseConvergeView(
    IReadOnlyList<PlatformReleaseConvergeTargetOutcomeView> Targets,
    string? ProposalId,
    string? Message);

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
