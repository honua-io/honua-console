using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Studio form-builder data source. Every method binds to the server-owned honua-server form package
/// lifecycle (honua-server#1184) through the Honua.Console.Contracts shim; there is no standing
/// in-memory form client in the merged result (Console Patterns Charter section 11). When no server
/// binding is configured, the unsupported implementation surfaces an explicit missing-binding state
/// rather than fabricating form data.
/// </summary>
public interface IStudioFormPackageDataSource
{
    /// <summary>Lists the server's form packages plus any binding/permission capability states.</summary>
    Task<StudioFormWorkspace> GetWorkspaceAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads an existing package's current version into editor state, or returns a fresh draft
    /// template when <paramref name="formId"/> is null/blank.
    /// </summary>
    Task<StudioFormEditorLoad> LoadAsync(string? formId, CancellationToken cancellationToken = default);

    /// <summary>Creates or updates the draft for the supplied editor state.</summary>
    Task<StudioFormCommandResult> SaveDraftAsync(
        StudioFormEditorState state,
        CancellationToken cancellationToken = default);

    /// <summary>Runs server publish-validation against the saved draft version.</summary>
    Task<StudioFormCommandResult> ValidateAsync(
        StudioFormEditorState state,
        CancellationToken cancellationToken = default);

    /// <summary>Publishes the saved draft version after the pre-publish gate passes.</summary>
    Task<StudioFormCommandResult> PublishAsync(
        StudioFormEditorState state,
        CancellationToken cancellationToken = default);

    /// <summary>Reopens a published version as a new editable draft.</summary>
    Task<StudioFormCommandResult> ReopenAsync(
        string formId,
        int version,
        CancellationToken cancellationToken = default);

    /// <summary>Reads the published offline/sync policy advertised for a package.</summary>
    Task<StudioFormOfflinePolicyView> GetOfflinePolicyAsync(
        string formId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads whether natural-language form generation is available on the bound server and which providers
    /// (local GIS model / Claude / GPT) are enabled+configured, or a binding state when the surface is
    /// unbound. Drives the "Form from prompt" provider selector and its availability state.
    /// </summary>
    Task<StudioFormAiCapability> GetGenerationCapabilityAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates (fresh) or refines (when the editor state already has fields) a form package from a
    /// natural-language prompt. The server grounds the proposal in the target service/layer schema and
    /// validates it before returning, so the outcome is either a server-produced document, a structured
    /// clarification request, an unsupported/refused turn, or a binding state - never a fabricated form.
    /// </summary>
    Task<StudioFormGenerationOutcome> GenerateAsync(
        StudioFormEditorState currentState,
        StudioFormGenerationRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Converts an uploaded XLSForm (ODK Collect) workbook into a fresh, unsaved form-builder draft by
    /// running the server-side Collect import path (Honua.Collect.Core XlsFormImporter). The console never
    /// reimplements the importer: it ships the workbook bytes to the admin import endpoint and maps the
    /// server-produced package document into editor state, surfacing the importer's diagnostics (unsupported
    /// question types, dropped constructs) rather than dropping them. Returns an unsupported/rejected status
    /// when the server lacks the import contract or rejects the workbook, or a binding state when unbound —
    /// never a fabricated form.
    /// </summary>
    Task<StudioFormImportOutcome> ImportXlsFormAsync(
        StudioFormImportRequest request,
        CancellationToken cancellationToken = default);
}
