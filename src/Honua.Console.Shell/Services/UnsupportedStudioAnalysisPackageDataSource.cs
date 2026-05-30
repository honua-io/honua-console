using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Analysis-builder data source used when no honua-server base address is configured. Every surface renders
/// an explicit missing-binding state instead of fabricating analysis data, keeping the merged runtime free
/// of a standing in-memory analysis client (Console Patterns Charter section 11). The analysis authoring
/// surface stays bound to the real honua-server analysis content/artifacts contract (honua-server#1182) and
/// the closed execution engine (honua-server#681/#721/#724) — or it shows this state.
/// </summary>
public sealed class UnsupportedStudioAnalysisPackageDataSource : IStudioAnalysisPackageDataSource
{
    private static readonly StudioAnalysisCapabilityState MissingBinding = new(
        "Analysis builder",
        "Missing binding",
        "Honua:Server:BaseUrl",
        "Configure Honua:Server:BaseUrl or HONUA_SERVER_BASE_URL so the analysis builder can bind the server-owned analysis content/artifacts contract (honua-server#1182) and the execution engine from honua-server.");

    public Task<StudioAnalysisWorkspace> GetWorkspaceAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new StudioAnalysisWorkspace([], [MissingBinding]));

    public Task<StudioAnalysisEditorLoad> LoadAsync(string? analysisId, CancellationToken cancellationToken = default) =>
        Task.FromResult(new StudioAnalysisEditorLoad(null, [MissingBinding]));

    public Task<StudioAnalysisCommandResult> SaveDraftAsync(
        StudioAnalysisPlanEditor plan,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(BindingFailure());

    public Task<StudioAnalysisCommandResult> EstimateAsync(
        StudioAnalysisPlanEditor plan,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(BindingFailure());

    public Task<StudioAnalysisCommandResult> PreviewAsync(
        StudioAnalysisPlanEditor plan,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(BindingFailure());

    public Task<StudioAnalysisCommandResult> SubmitAsync(
        StudioAnalysisPlanEditor plan,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(BindingFailure());

    private static StudioAnalysisCommandResult BindingFailure() =>
        new(false, MissingBinding.Detail, Issue: MissingBinding);
}
