using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public interface IPublishingWorkspaceDataSource
{
    Task<PublishingWorkspace> GetWorkspaceAsync(CancellationToken cancellationToken = default);

    Task<PublishingLookupResult> LookupAsync(string publicationId, CancellationToken cancellationToken = default);

    Task<PublishingLookupResult> RepublishAsync(
        string publicationId,
        PublishingRepublishCommand command,
        CancellationToken cancellationToken = default);

    Task<PublishingLookupResult> RollbackAsync(
        string publicationId,
        PublishingRollbackCommand command,
        CancellationToken cancellationToken = default);
}
