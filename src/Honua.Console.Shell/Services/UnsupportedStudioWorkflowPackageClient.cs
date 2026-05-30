using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// The runtime default when no honua-server base address is configured. Every operation returns a
/// missing-binding state so the Studio workflow editor renders the shared blocked surface (charter §5/§11)
/// instead of seeded workflow data. Mirrors <see cref="UnsupportedStudioAuthoringShell"/> and
/// <see cref="UnsupportedOperateTransitionDataSource"/>.
/// </summary>
public sealed class UnsupportedStudioWorkflowPackageClient : IStudioWorkflowPackageClient
{
    private const string Surface = "Studio workflow.package";

    private static readonly StudioWorkflowBindingState MissingBinding = new(
        Surface,
        "Missing binding",
        "Honua:Server:BaseUrl",
        "Configure Honua:Server:BaseUrl or HONUA_SERVER_BASE_URL so Studio can bind the server-owned GP/ETL "
            + "workflow package, node registry, dry-run, and publication contract from honua-server (#1185).");

    public Task<StudioWorkflowEditorContext> OpenEditorAsync(
        string? draftId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new StudioWorkflowEditorContext(MissingBinding, [], Draft: null));

    public Task<IReadOnlyList<StudioWorkflowDraftSummary>> ListDraftsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<StudioWorkflowDraftSummary>>([]);

    public Task<IReadOnlyList<StudioWorkflowNodeDefinition>> ListNodeDefinitionsAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<StudioWorkflowNodeDefinition>>([]);

    public Task<StudioWorkflowPackageDraft> CreateDraftAsync(CancellationToken cancellationToken = default) =>
        // The editor never reaches a create path when unbound (OpenEditorAsync returns the blocked surface
        // first), but the contract must not fabricate a server draft.
        throw new InvalidOperationException(
            "Studio workflow package data is not bound to a honua-server; cannot create a draft.");

    public Task<StudioWorkflowPackageDraft?> GetDraftAsync(
        string draftId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<StudioWorkflowPackageDraft?>(null);

    public Task<StudioWorkflowSaveResult> SaveVersionAsync(
        StudioWorkflowPackageDraft draft,
        string changeNote,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new StudioWorkflowSaveResult { BindingState = MissingBinding });

    public Task<StudioWorkflowDryRunResult> DryRunAsync(
        StudioWorkflowPackageDraft draft,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new StudioWorkflowDryRunResult { Status = "blocked", BindingState = MissingBinding });

    public Task<StudioWorkflowPublishResult> PublishAsync(
        StudioWorkflowPackageDraft draft,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new StudioWorkflowPublishResult { Status = "blocked", BindingState = MissingBinding });

    public Task<StudioWorkflowJobEvidence?> GetJobEvidenceAsync(
        string jobId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<StudioWorkflowJobEvidence?>(null);

    public Task<StudioWorkflowRunHistory> ListRunHistoryAsync(
        string contentItemId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(StudioWorkflowRunHistory.Blocked(MissingBinding));
}
