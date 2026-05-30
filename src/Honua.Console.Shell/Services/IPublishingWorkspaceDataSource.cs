using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Reads and drives the Operate publishing workspace (publication matrix, review records, and the
/// republish/rollback lifecycle) for <c>/operate/publishing</c>. The merged runtime binds this to the
/// honua-server content publication registry (honua-server#1183, shipped) via the
/// <c>Honua.Console.Contracts</c> shim; there is no standing in-memory implementation in the merged
/// build (Console Patterns Charter section 11). When no server base URL is configured the data source
/// returns a workspace whose only content is a missing-binding <see cref="OperateCapabilityState"/>.
/// </summary>
public interface IPublishingWorkspaceDataSource
{
    /// <summary>
    /// Loads the workspace: the publication matrix and review records for the configured publication
    /// ids, plus any capability states accumulated while reading them.
    /// </summary>
    Task<PublishingWorkspace> GetWorkspaceAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Looks up a single publication by id and projects its route state into a review (resource, slot,
    /// generated endpoints, catalog registration, policy, warnings, rollback class, provenance, and
    /// evidence deep links) plus its version history. Drives the quick-publish / author-first lookup.
    /// </summary>
    Task<PublishingLookupResult> LookupAsync(string publicationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Republishes a publication (new immutable version, active pointer advanced) and returns the
    /// refreshed lookup result, or a capability state when the server rejects/blocks the operation.
    /// </summary>
    Task<PublishingLookupResult> RepublishAsync(
        string publicationId,
        PublishingRepublishCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Rolls a publication's active route pointer back to an earlier version and returns the refreshed
    /// lookup result, or a capability state when the server rejects/blocks the operation.
    /// </summary>
    Task<PublishingLookupResult> RollbackAsync(
        string publicationId,
        PublishingRollbackCommand command,
        CancellationToken cancellationToken = default);
}
