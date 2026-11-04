using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

// The in-memory implementation is test-only and must not be the data source for a deployed surface.
public interface IStudioWorkflowPackageClient
{
    Task<StudioWorkflowEditorContext> OpenEditorAsync(
        string? draftId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StudioWorkflowDraftSummary>> ListDraftsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StudioWorkflowNodeDefinition>> ListNodeDefinitionsAsync(
        CancellationToken cancellationToken = default);

    Task<StudioWorkflowPackageDraft> CreateDraftAsync(CancellationToken cancellationToken = default);

    Task<StudioWorkflowPackageDraft?> GetDraftAsync(
        string draftId,
        CancellationToken cancellationToken = default);

    // A saved version number only increases.
    Task<StudioWorkflowSaveResult> SaveVersionAsync(
        StudioWorkflowPackageDraft draft,
        string changeNote,
        CancellationToken cancellationToken = default);

    Task<StudioWorkflowDryRunResult> DryRunAsync(
        StudioWorkflowPackageDraft draft,
        CancellationToken cancellationToken = default);

    // A blocked publish returns no job or publication id. Publish uses a saved version, and Operate evidence links are emitted only for a queued job.
    Task<StudioWorkflowPublishResult> PublishAsync(
        StudioWorkflowPackageDraft draft,
        CancellationToken cancellationToken = default);

    Task<StudioWorkflowJobEvidence?> GetJobEvidenceAsync(
        string jobId,
        CancellationToken cancellationToken = default);

    // Runs are listed newest first.
    Task<StudioWorkflowRunHistory> ListRunHistoryAsync(
        string contentItemId,
        CancellationToken cancellationToken = default);

}
