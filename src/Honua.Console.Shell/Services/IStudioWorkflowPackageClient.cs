using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public interface IStudioWorkflowPackageClient
{
    Task<IReadOnlyList<StudioWorkflowDraftSummary>> ListDraftsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StudioWorkflowNodeDefinition>> ListNodeDefinitionsAsync(
        CancellationToken cancellationToken = default);

    Task<StudioWorkflowPackageDraft> CreateDraftAsync(CancellationToken cancellationToken = default);

    Task<StudioWorkflowPackageDraft?> GetDraftAsync(
        string draftId,
        CancellationToken cancellationToken = default);

    Task<StudioWorkflowSaveResult> SaveVersionAsync(
        StudioWorkflowPackageDraft draft,
        string changeNote,
        CancellationToken cancellationToken = default);

    Task<StudioWorkflowDryRunResult> DryRunAsync(
        StudioWorkflowPackageDraft draft,
        CancellationToken cancellationToken = default);

    Task<StudioWorkflowPublishResult> PublishAsync(
        StudioWorkflowPackageDraft draft,
        CancellationToken cancellationToken = default);

    Task<StudioWorkflowJobEvidence?> GetJobEvidenceAsync(
        string jobId,
        CancellationToken cancellationToken = default);
}
