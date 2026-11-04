using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public sealed class UnsupportedPublishingWorkspaceDataSource : IPublishingWorkspaceDataSource
{
    private static readonly OperateCapabilityState MissingBinding = new(
        "Publishing",
        "Missing binding",
        "Honua:Server:BaseUrl",
        "Configure Honua:Server:BaseUrl or HONUA_SERVER_BASE_URL so the publishing workspace can "
        + "read the publication registry from honua-server (honua-server#1183). No mock data source is "
        + "merged in the interim.");

    private static readonly PublishingWorkspace Workspace = new(
        Matrix: [],
        Reviews: [],
        CapabilityStates: [MissingBinding]);

    public Task<PublishingWorkspace> GetWorkspaceAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Workspace);

    public Task<PublishingLookupResult> LookupAsync(
        string publicationId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(PublishingLookupResult.FromCapabilityState(MissingBinding));

    public Task<PublishingLookupResult> RepublishAsync(
        string publicationId,
        PublishingRepublishCommand command,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(PublishingLookupResult.FromCapabilityState(MissingBinding));

    public Task<PublishingLookupResult> RollbackAsync(
        string publicationId,
        PublishingRollbackCommand command,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(PublishingLookupResult.FromCapabilityState(MissingBinding));
}
