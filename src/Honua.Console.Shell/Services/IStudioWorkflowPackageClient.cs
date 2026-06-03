using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Client contract for the Studio workflow-package authoring slice (honua-console#40):
/// list/create drafts, save versions, dry-run, publish, and read job evidence.
/// </summary>
/// <remarks>
/// <para>
/// <b>Placeholder contract.</b> The only shipped implementation today is
/// <see cref="InMemoryStudioWorkflowPackageClient"/>, a transient in-memory scaffold.
/// Per <c>docs/migration/CONSOLE_PATTERNS_CHARTER.md</c> section 11 ("Real-server
/// integration and no standing mocks"), workflow.package data is server-owned and must
/// ultimately bind to <c>honua-server</c> through <c>honua-sdk-dotnet</c> projections (or
/// the <c>Honua.Console.Contracts</c> shim boundary until those land). The workflow
/// surface stays blocked on the server/SDK contract; the in-memory client must never be
/// the merged data source for a deployed surface.
/// </para>
/// <para>
/// A future replacement MUST preserve the contract nuances the in-memory implementation
/// asserts, because Console UX and the <c>smoke/parity/workflow.mjs</c> harness depend on
/// them:
/// <list type="bullet">
/// <item><description>
/// <see cref="PublishAsync"/> is gated by <see cref="StudioWorkflowValidationIssue"/>s of
/// severity <c>error</c> and (for endpoint publications) parameter validation; a blocked
/// publish returns <c>Status = "blocked"</c> with empty job/publication identifiers and no
/// Operate evidence queued.
/// </description></item>
/// <item><description>
/// <see cref="PublishAsync"/> auto-saves a new content version when the draft changed since
/// its last saved version, so the published version id always matches stored state.
/// </description></item>
/// <item><description>
/// <see cref="SaveVersionAsync"/> is monotonic: the version number only ever increases.
/// </description></item>
/// <item><description>
/// Operate deep links (<c>/operate/jobs/...</c>, <c>/operate/events?jobId=...</c>) are
/// emitted only for jobs that were actually queued.
/// </description></item>
/// </list>
/// </para>
/// </remarks>
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

    /// <summary>
    /// Reads whether natural-language workflow generation is available on the bound server and which
    /// providers (local GIS model / Claude / GPT) are enabled+configured, or a binding state when the
    /// surface is unbound. Drives the "Workflow from prompt" provider selector and its availability state.
    /// </summary>
    Task<StudioWorkflowAiCapability> GetGenerationCapabilityAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates (fresh) or refines (when <paramref name="currentDraft"/> already has nodes) a workflow
    /// package from a natural-language prompt. The server grounds the proposal in the node registry and
    /// validates it before returning, so the outcome is either a server-produced graph, a structured
    /// clarification request, an unsupported/refused turn, or a binding state - never a fabricated graph.
    /// </summary>
    Task<StudioWorkflowGenerationOutcome> GenerateAsync(
        StudioWorkflowPackageDraft currentDraft,
        StudioWorkflowGenerationRequest request,
        CancellationToken cancellationToken = default);
}
