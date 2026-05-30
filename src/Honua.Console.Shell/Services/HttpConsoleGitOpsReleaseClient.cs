using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Binds the GitOps metadata release visualization surface to a real honua-server
/// through the GitOps metadata release contracts (honua-server#1163/#1164), via a
/// thin shim HttpClient behind <c>Honua.Console.Contracts</c> until
/// <c>honua-sdk-dotnet</c> projects those contracts. Base address and bearer token
/// come from the active <see cref="ConsoleEnvironmentProfile"/> and its account
/// session, reusing the connection pattern from
/// <see cref="HttpConsoleOperateObservabilityClient"/>.
///
/// Charter section 11 (no standing mock for server-owned data) is preserved: this
/// client never returns seeded release data. When no environment profile is bound,
/// every read returns a missing-binding result. The concrete request/deserialize
/// path is wired once the server release-package read endpoint contract is
/// published; until then the read reports the surface as unsupported by the
/// connected server rather than fabricating a proposal.
/// </summary>
public sealed class HttpConsoleGitOpsReleaseClient : IConsoleGitOpsReleaseClient
{
    private const string PendingContractMessage =
        "The connected honua-server does not yet expose the GitOps metadata release " +
        "package contract (honua-server#1163/#1164). This surface renders from live " +
        "release data once that contract is bound.";

    private readonly IConsoleEnvironmentProfileStore _profileStore;

    public HttpConsoleGitOpsReleaseClient(IConsoleEnvironmentProfileStore profileStore)
    {
        _profileStore = profileStore ?? throw new ArgumentNullException(nameof(profileStore));
    }

    public async Task<OperateSectionResult<IReadOnlyList<GitOpsReleaseProposal>>> GetReleaseProposalsAsync(
        CancellationToken cancellationToken = default)
    {
        var profile = await _profileStore.GetActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            return OperateSectionResult<IReadOnlyList<GitOpsReleaseProposal>>.Denied(
                OperateSectionStatus.Unavailable,
                "No active environment profile is selected. Connect an environment to load GitOps releases.");
        }

        // The release-package read endpoint contract (honua-server#1163/#1164) is
        // not yet bound, so report the surface as unsupported by the connected
        // server rather than returning a standing mock proposal (charter section 11).
        return OperateSectionResult<IReadOnlyList<GitOpsReleaseProposal>>.Denied(
            OperateSectionStatus.Unsupported,
            PendingContractMessage);
    }

    public async Task<OperateSectionResult<GitOpsReleaseProposal>> GetReleaseProposalAsync(
        string releasePackageId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(releasePackageId);

        var profile = await _profileStore.GetActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            return OperateSectionResult<GitOpsReleaseProposal>.Denied(
                OperateSectionStatus.Unavailable,
                "No active environment profile is selected. Connect an environment to load GitOps releases.");
        }

        return OperateSectionResult<GitOpsReleaseProposal>.Denied(
            OperateSectionStatus.Unsupported,
            PendingContractMessage);
    }
}
