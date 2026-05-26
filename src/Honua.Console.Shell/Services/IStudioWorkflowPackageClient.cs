using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public interface IStudioWorkflowPackageClient
{
    /// <summary>
    /// Opens the workflow editor for <paramref name="draftId"/> (or a new local draft when null/"new"),
    /// returning the server node registry and the draft in one call - or a
    /// <see cref="StudioWorkflowBindingState"/> when the surface is blocked / unbound.
    /// </summary>
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
