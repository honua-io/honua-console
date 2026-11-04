using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public interface IStudioReportPublicationDataSource
{
    Task<StudioReportPublicationLoad> LoadAsync(
        string publicationId,
        CancellationToken cancellationToken = default);

    // The server hashes the serialized report. Call this only after the pre-publish gate passes.
    Task<StudioReportCommandResult> PublishAsync(
        StudioReportEditorState state,
        CancellationToken cancellationToken = default);

    // Rollback moves the active pointer to an earlier version and does not create a version.
    Task<StudioReportCommandResult> RollbackAsync(
        string publicationId,
        string targetVersionId,
        string? expectedEtag = null,
        CancellationToken cancellationToken = default);

    // A policy update does not create a content version; the server records it as an audited event.
    Task<StudioReportCommandResult> UpdatePolicyAsync(
        string publicationId,
        string visibility,
        bool embeddable,
        string? expectedEtag = null,
        CancellationToken cancellationToken = default);

}
