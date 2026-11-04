using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public sealed class UnsupportedConsoleDeployApprovalClient : IConsoleDeployApprovalClient
{
    private const string Message =
        "Deploy approvals are not configured for this Console build. Connect an environment to review and approve "
        + "paused deploy-control operations.";

    public Task<OperateSectionResult<DeployOperationProposal>> GetOperationAsync(
        string operationId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(OperateSectionResult<DeployOperationProposal>.Denied(OperateSectionStatus.Unsupported, Message));

    public Task<OperateSectionResult<IReadOnlyList<DeployOperationProposal>>> ListPendingAsync(
        IReadOnlyList<string> operationIds,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(OperateSectionResult<IReadOnlyList<DeployOperationProposal>>.Denied(
            OperateSectionStatus.Unsupported,
            Message));

    public Task<OperateSectionResult<DeployOperationProposal>> SubmitAsync(
        string operationId,
        string? reason,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(OperateSectionResult<DeployOperationProposal>.Denied(OperateSectionStatus.Unsupported, Message));

    public Task<OperateSectionResult<DeployOperationProposal>> RollbackAsync(
        string operationId,
        string? reason,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(OperateSectionResult<DeployOperationProposal>.Denied(OperateSectionStatus.Unsupported, Message));
}
