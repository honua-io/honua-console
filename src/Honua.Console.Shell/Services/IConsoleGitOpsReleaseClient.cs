using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Reads the GitOps metadata release visualization surface (release proposals and
/// their semantic resource diffs) from a real honua-server through the GitOps
/// metadata release contracts (honua-server#1163/#1164). The release-operation
/// lifecycle (timeline/rollback execution, honua-server#1165) is intentionally
/// not part of this read contract and stays blocked until that server contract
/// lands.
///
/// Each read returns an <see cref="OperateSectionResult{T}"/> carrying a status
/// that drives the shared missing/forbidden/unsupported/unavailable surfaces,
/// mirroring <see cref="IConsoleOperateObservabilityClient"/>. Per the Console
/// Patterns Charter section 11, server-owned release data binds to a live server
/// (or a thin shim HttpClient) and never to a standing in-memory/mock source; when
/// no environment is bound the read returns a missing-binding result rather than
/// seeded data.
/// </summary>
public interface IConsoleGitOpsReleaseClient
{
    /// <summary>
    /// Lists the release proposals visible to the active environment profile.
    /// </summary>
    Task<OperateSectionResult<IReadOnlyList<GitOpsReleaseProposal>>> GetReleaseProposalsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a single release proposal summary (design view #1) on demand.
    /// </summary>
    Task<OperateSectionResult<GitOpsReleaseProposal>> GetReleaseProposalAsync(
        string releasePackageId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the full release detail surface for one release package id: the proposal
    /// summary and semantic diff (release package + GitOps manifest, honua-server#1163),
    /// the environment matrix/drift, and the CI/GitOps timeline and rollback readiness
    /// (release-operation lifecycle, honua-server#1165). The operation lifecycle is an
    /// independent sub-read, so a missing operation surfaces its own state rather than
    /// failing the whole detail read.
    /// </summary>
    Task<OperateSectionResult<GitOpsReleaseDetail>> GetReleaseDetailAsync(
        string releasePackageId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the coordinated platform-upgrade release operation for a release package id (Demo C,
    /// honua-server#97): the container image + DB/schema change + metadata semantic diff as one
    /// ordered, gated, rollback-able op, with its ordered step timeline and rollback readiness. This
    /// is an independent sub-read of the release detail surface, so a missing coordinated op surfaces
    /// its own state rather than failing the whole detail read.
    /// </summary>
    Task<OperateSectionResult<GitOpsCoordinatedRelease>> GetCoordinatedReleaseAsync(
        string releasePackageId,
        CancellationToken cancellationToken = default);
}
