using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Drives the shared Studio authoring surface (<c>/studio</c>). Server-owned draft/package lifecycle,
/// validation, and preview-planning bind to honua-server (#1180/#1181); prompt and structured
/// clarification stay Console-local UX. All operations are asynchronous because the runtime default is
/// server-backed (see <c>ServerStudioAuthoringShell</c>); the in-memory implementation is for explicit
/// demo composition and unit tests only.
/// </summary>
public interface IStudioAuthoringShell
{
    Task<StudioAuthoringSession> CreateInitialSessionAsync(CancellationToken cancellationToken = default);

    Task<StudioAuthoringSession> SelectWorkflowAsync(
        StudioAuthoringSession session,
        string workflowId,
        CancellationToken cancellationToken = default);

    /// <summary>Creates (or replaces) the server draft for the selected family from the current prompt.</summary>
    Task<StudioAuthoringSession> GeneratePackageAsync(
        StudioAuthoringSession session,
        string workflowId,
        string prompt,
        CancellationToken cancellationToken = default);

    Task<StudioAuthoringSession> ApplyClarificationAsync(
        StudioAuthoringSession session,
        string questionId,
        string choiceId,
        CancellationToken cancellationToken = default);

    /// <summary>Runs server validation and renders the real validation summary.</summary>
    Task<StudioAuthoringSession> ValidateAsync(
        StudioAuthoringSession session,
        CancellationToken cancellationToken = default);

    /// <summary>Runs server preview-planning (not preview execution) and renders the real plan.</summary>
    Task<StudioAuthoringSession> PreviewPlanAsync(
        StudioAuthoringSession session,
        CancellationToken cancellationToken = default);

    /// <summary>Saves the draft as an immutable content version.</summary>
    Task<StudioAuthoringSession> SaveVersionAsync(
        StudioAuthoringSession session,
        CancellationToken cancellationToken = default);

    /// <summary>Publishes the current saved content version.</summary>
    Task<StudioAuthoringSession> PublishAsync(
        StudioAuthoringSession session,
        CancellationToken cancellationToken = default);
}
