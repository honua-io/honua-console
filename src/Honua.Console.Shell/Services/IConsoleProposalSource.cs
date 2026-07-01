using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// One provider of proposals the approval inbox aggregates (issue #193). The console surfaces a
/// single GIS-department work queue over TWO ownership domains (honua-server #1690's locked
/// split): honua-server owns admin/deploy/metadata/seed proposals, and honua-devops owns
/// gitops/infra + deliverable proposals through its console bridge. Each source normalizes its
/// domain onto the shared <see cref="ConsoleProposalSummary"/> projection (tagged with its
/// <see cref="Source"/>), and <see cref="ConsoleApprovalInboxClient"/> merges them into one
/// inbox.
///
/// A source returns an <see cref="OperateSectionResult{T}"/> whose status drives the shared
/// missing / forbidden / unsupported / unavailable surfaces. Per the Console Patterns Charter
/// (section 11) a source binds to a live owning system and never to a standing in-memory / mock
/// source; a source that is not reachable yet degrades gracefully (an empty allowed result)
/// rather than fabricating a queue, so it never blocks the reachable sources.
/// </summary>
public interface IConsoleProposalSource
{
    /// <summary>The ownership domain this source projects (server vs devops-bridge).</summary>
    ConsoleProposalSource Source { get; }

    /// <summary>
    /// Lists this source's active proposals as the shared summary projection, tagged with
    /// <see cref="Source"/>. Optionally filtered by the owning system's status/kind parameters.
    /// </summary>
    Task<OperateSectionResult<IReadOnlyList<ConsoleProposalSummary>>> ListAsync(
        string? status = null,
        string? kind = null,
        CancellationToken cancellationToken = default);
}
