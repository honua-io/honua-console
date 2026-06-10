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

    /// <summary>
    /// Generates (or refines) an analysis package from a natural-language prompt against the server's
    /// analysis/content/generate contract. The server grounds the proposal and validates it before
    /// returning, so the outcome only ever carries a server-produced analysis (status=="generated", the plan
    /// card hydrates from the returned AnalysisPackageContent), a structured clarification request, or an
    /// honest unavailable/refused/blocked state — never a fabricated analysis (Console Patterns Charter
    /// section 11).
    /// </summary>
    Task<StudioAnalysisGenerationOutcome> GenerateAsync(
        StudioAnalysisPlanEditor currentPlan,
        StudioAnalysisGenerationRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Builds an instant, real-data baseline analysis from the live catalog (no model call) so the builder
    /// can render a starting result the moment a prompt is sent, rather than blocking the operator on a model
    /// round-trip that can take minutes and may report "unsupported" on this server. The baseline binds a real
    /// service+layer (a distribution of the layer the prompt names, or the first available source); it never
    /// fabricates data (Charter §11). Returns null when no catalog source is available. Test doubles inherit
    /// the null default.
    /// </summary>
    Task<StudioAnalysisPlanEditor?> SeedBaselineAsync(
        string? prompt,
        CancellationToken cancellationToken = default)
        => Task.FromResult<StudioAnalysisPlanEditor?>(null);
}
