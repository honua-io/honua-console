using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public interface IConsoleGitOpsReleaseClient
{
    Task<OperateSectionResult<IReadOnlyList<GitOpsReleaseProposal>>> GetReleaseProposalsAsync(
        CancellationToken cancellationToken = default);

    Task<OperateSectionResult<GitOpsReleaseProposal>> GetReleaseProposalAsync(
        string releasePackageId,
        CancellationToken cancellationToken = default);

    // A missing release operation is its own state and does not fail the rest of the detail read.
    Task<OperateSectionResult<GitOpsReleaseDetail>> GetReleaseDetailAsync(
        string releasePackageId,
        CancellationToken cancellationToken = default);

    // A missing coordinated operation is its own state and does not fail the detail read.
    Task<OperateSectionResult<GitOpsCoordinatedRelease>> GetCoordinatedReleaseAsync(
        string releasePackageId,
        CancellationToken cancellationToken = default);
}
