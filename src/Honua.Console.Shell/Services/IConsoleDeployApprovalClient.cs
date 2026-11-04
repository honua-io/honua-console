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

    Task<OperateSectionResult<DeployOperationProposal>> SubmitAsync(
        string operationId,
        string? reason,
        CancellationToken cancellationToken = default);

    // A data-affecting rollback stays behind the server approval gate, and the caller confirms it before this method runs; a denial is Forbidden.
    Task<OperateSectionResult<DeployOperationProposal>> RollbackAsync(
        string operationId,
        string? reason,
        CancellationToken cancellationToken = default);
}
