using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// The runtime default when no honua-server / Collect projection is configured. Every operation returns a
/// missing-binding state so the automation authoring + versioning surface renders the shared blocked surface
/// (charter §5/§11) instead of seeded automation data. Mirrors
/// <see cref="UnsupportedStudioWorkflowPackageClient"/>.
/// </summary>
public sealed class UnsupportedCollectAutomationClient : ICollectAutomationClient
{
    private const string Surface = "Collect automation";

    private static readonly CollectAutomationBindingState MissingBinding = new(
        Surface,
        "Missing binding",
        "Honua:Server:BaseUrl",
        "Configure Honua:Server:BaseUrl or HONUA_SERVER_BASE_URL so Console can bind the Collect automation "
            + "content + version projection over the shipped Data Events engine (Honua.Collect.Core).");

    public Task<IReadOnlyList<CollectAutomationSummary>> ListAutomationsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<CollectAutomationSummary>>([]);

    public Task<CollectAutomationEditorContext> OpenEditorAsync(string? draftId, CancellationToken cancellationToken = default) =>
        Task.FromResult(new CollectAutomationEditorContext(MissingBinding, Draft: null));

    public Task<CollectAutomationSaveResult> SaveVersionAsync(
        CollectAutomationDraft draft,
        string changeNote,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new CollectAutomationSaveResult { BindingState = MissingBinding });

    public Task<CollectAutomationVersionHistory> ListVersionsAsync(
        string contentItemId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(CollectAutomationVersionHistory.Blocked(MissingBinding));

    public Task<CollectAutomationDraft?> GetVersionAsync(
        string contentItemId,
        string versionId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<CollectAutomationDraft?>(null);

    public Task<CollectAutomationRestoreResult> RestoreVersionAsync(
        string contentItemId,
        string versionId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new CollectAutomationRestoreResult { BindingState = MissingBinding });
}
