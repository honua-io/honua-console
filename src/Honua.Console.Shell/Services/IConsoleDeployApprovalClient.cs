using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

// The server approval gate is never bypassed. ListPendingAsync is the tracked-id read when the paged operations list is unsupported, and an empty id set returns an empty allowed result rather than invented operations.
public interface IConsoleDeployApprovalClient
{
    Task<OperateSectionResult<DeployOperationProposal>> GetOperationAsync(
        string operationId,
        CancellationToken cancellationToken = default);

    // An unreadable id is skipped and does not fail the rest of the list.
    Task<OperateSectionResult<IReadOnlyList<DeployOperationProposal>>> ListPendingAsync(
        IReadOnlyList<string> operationIds,
        CancellationToken cancellationToken = default);

    Task<OperateSectionResult<ConsoleDeployApprovalReview>> OpenSubmitReviewAsync(
        string operationId,
        CancellationToken cancellationToken = default);

    Task<OperateSectionResult<DeployOperationProposal>> SubmitAsync(
        ConsoleDeployApprovalReview review,
        string? reason,
        CancellationToken cancellationToken = default);

    Task<OperateSectionResult<DeployOperationProposal>> SubmitAsync(
        string operationId,
        string? reason,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(OperateSectionResult<DeployOperationProposal>.Denied(
            OperateSectionStatus.Unsupported,
            "Deploy submission is not supported by this client."));

    Task<OperateSectionResult<DeployOperationProposal>> SubmitAsync(
        string operationId,
        string? reason,
        ConsoleDeployReviewedOperation? reviewed,
        CancellationToken cancellationToken = default) =>
        SubmitAsync(operationId, reason, cancellationToken);

    Task<OperateSectionResult<DeployOperationProposal>> PromoteAsync(
        string operationId,
        string? reason,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(OperateSectionResult<DeployOperationProposal>.Denied(
            OperateSectionStatus.Unsupported,
            "Deploy promotion is not supported by this client."));

    Task<OperateSectionResult<DeployOperationProposal>> PromoteAsync(
        string operationId,
        string? reason,
        ConsoleDeployReviewedOperation? reviewed,
        CancellationToken cancellationToken = default) =>
        PromoteAsync(operationId, reason, cancellationToken);

    // A data-affecting rollback stays behind the server approval gate, and the caller confirms it before this method runs; a denial is Forbidden.
    Task<OperateSectionResult<DeployOperationProposal>> RollbackAsync(
        string operationId,
        string? reason,
        CancellationToken cancellationToken = default);
}

public sealed record ConsoleDeployReviewedOperation(
    long Version,
    string? PlanHash,
    string? DesiredRevision = null)
{
    public static ConsoleDeployReviewedOperation? From(DeployOperationProposal? proposal)
    {
        if (proposal is null)
        {
            return null;
        }

        var desired = string.IsNullOrWhiteSpace(proposal.DesiredRevision)
            || string.Equals(proposal.DesiredRevision, "unknown", StringComparison.OrdinalIgnoreCase)
            ? null
            : proposal.DesiredRevision;
        if (proposal.Version <= 0 && string.IsNullOrWhiteSpace(proposal.PlanHash) && desired is null)
        {
            return null;
        }

        return new ConsoleDeployReviewedOperation(proposal.Version, proposal.PlanHash, desired);
    }
}
