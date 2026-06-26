using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Client contract for the Collect automation authoring + versioning surface (honua-console#219): list,
/// open, create, save (versioned), restore a prior version, and read the version history of automations
/// that drive the shipped Collect Data Events engine (<c>Honua.Collect.Core</c>, honua-collect PRs
/// #58/#84/#94).
/// </summary>
/// <remarks>
/// <para>
/// <b>The engine is not re-implemented here.</b> Triggers, sandboxed expression conditions, the
/// set/compute/validate/tag/notify/http/open-url action seam, the deterministic cascade + loop guard, and
/// the durable outbox all ship in Collect Core. This Console contract only composes/edits/versions the
/// automation body the engine runs. Per <c>docs/migration/CONSOLE_PATTERNS_CHARTER.md</c> §11 ("Real-server
/// integration and no standing mocks"), automation content is server/Collect-owned and must ultimately bind
/// to a real projection; the merged runtime default is the missing-binding
/// <see cref="UnsupportedCollectAutomationClient"/>. The in-memory client is test/demo-only and is never the
/// merged data source for a deployed surface.
/// </para>
/// <para>
/// A future server-backed replacement MUST preserve the contract nuances the in-memory implementation
/// asserts:
/// <list type="bullet">
/// <item><description>
/// <see cref="SaveVersionAsync"/> is monotonic and only commits an immutable version when the body passes
/// validation; a validation failure returns issues with no version id and keeps the draft dirty.
/// </description></item>
/// <item><description>
/// <see cref="RestoreVersionAsync"/> never mutates a prior version in place; it commits a NEW version whose
/// body equals the restored one, preserving an append-only history.
/// </description></item>
/// </list>
/// </para>
/// </remarks>
public interface ICollectAutomationClient
{
    /// <summary>
    /// Lists the automation drafts available to the operator (newest first), or an empty list when unbound.
    /// </summary>
    Task<IReadOnlyList<CollectAutomationSummary>> ListAutomationsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens the automation editor for <paramref name="draftId"/> (or a new local draft when null/"new"),
    /// returning the draft - or a <see cref="CollectAutomationBindingState"/> when the surface is unbound.
    /// </summary>
    Task<CollectAutomationEditorContext> OpenEditorAsync(string? draftId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Commits a new immutable version of <paramref name="draft"/> with <paramref name="changeNote"/>. The
    /// result carries the new version id on success, or validation issues with no version id when the body
    /// fails validation, or a binding state when unbound.
    /// </summary>
    Task<CollectAutomationSaveResult> SaveVersionAsync(
        CollectAutomationDraft draft,
        string changeNote,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the append-only version history for a saved automation content item (newest first), or a
    /// binding state when unbound, or an empty history for a never-saved draft.
    /// </summary>
    Task<CollectAutomationVersionHistory> ListVersionsAsync(
        string contentItemId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the immutable body of a prior version so the editor can preview it before restoring, or null
    /// when the version cannot be resolved / the surface is unbound.
    /// </summary>
    Task<CollectAutomationDraft?> GetVersionAsync(
        string contentItemId,
        string versionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Restores <paramref name="versionId"/> by committing a NEW version whose body equals it (append-only;
    /// the prior version is never mutated). Returns a binding state when unbound.
    /// </summary>
    Task<CollectAutomationRestoreResult> RestoreVersionAsync(
        string contentItemId,
        string versionId,
        CancellationToken cancellationToken = default);
}
