using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Studio analysis-builder data source (/studio/analysis, honua-console#53). Every method binds to the
/// server-owned honua-server analysis content/artifacts contract (honua-server#1182) and the closed
/// execution engine (honua-server#681/#721/#724) through the Honua.Console.Contracts shim or
/// honua-sdk-dotnet; there is no standing in-memory analysis client in the merged result (Console Patterns
/// Charter section 11). When no server binding is configured, the unsupported implementation surfaces an
/// explicit missing-binding state rather than fabricating analysis data.
/// </summary>
public interface IStudioAnalysisPackageDataSource
{
    /// <summary>Lists the server's analysis packages plus any binding/permission capability states.</summary>
    Task<StudioAnalysisWorkspace> GetWorkspaceAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads an existing package's current version into plan-card state, or returns a fresh draft
    /// template when <paramref name="analysisId"/> is null/blank.
    /// </summary>
    Task<StudioAnalysisEditorLoad> LoadAsync(string? analysisId, CancellationToken cancellationToken = default);

    /// <summary>Creates or updates the draft for the supplied plan state.</summary>
    Task<StudioAnalysisCommandResult> SaveDraftAsync(
        StudioAnalysisPlanEditor plan,
        CancellationToken cancellationToken = default);

    /// <summary>Requests a server runtime/cost compute estimate for the saved plan.</summary>
    Task<StudioAnalysisCommandResult> EstimateAsync(
        StudioAnalysisPlanEditor plan,
        CancellationToken cancellationToken = default);

    /// <summary>Submits a preview (dry-run) job for the saved plan.</summary>
    Task<StudioAnalysisCommandResult> PreviewAsync(
        StudioAnalysisPlanEditor plan,
        CancellationToken cancellationToken = default);

    /// <summary>Submits the saved plan as an execution job once the pre-submit gate passes.</summary>
    Task<StudioAnalysisCommandResult> SubmitAsync(
        StudioAnalysisPlanEditor plan,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves a result artifact produced by the execution engine into the submitted job's result-artifact
    /// panel, including the downstream content families the artifact can become (AC#3). The execution engine
    /// runs asynchronously and the bound contract exposes no list-job-artifacts route, so the artifact id is
    /// supplied by the operator (from the job logs / downstream surface) and resolved through the live
    /// <c>/api/v1/analysis/artifacts/{artifactId}</c> route — never fabricated.
    /// </summary>
    Task<StudioAnalysisCommandResult> ResolveArtifactAsync(
        string artifactId,
        StudioAnalysisPlanEditor plan,
        CancellationToken cancellationToken = default);
}
