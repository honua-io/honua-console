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

    /// <summary>
    /// Lists the execution history for a saved workflow content item (newest run first): dry-runs, published
    /// runs, and scheduled runs with their queued/running/succeeded/failed state, rejected-row counts, and
    /// Operate job/provenance links. Returns a <see cref="StudioWorkflowRunHistory"/> carrying a binding state
    /// when the surface is unbound, or an empty history for a draft that has never been saved/run.
    /// </summary>
    Task<StudioWorkflowRunHistory> ListRunHistoryAsync(
        string contentItemId,
        CancellationToken cancellationToken = default);
}
