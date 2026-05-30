using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Dashboard-builder data source used when no honua-server base address is configured. Every surface
/// renders an explicit missing-binding state instead of fabricating dashboard data, keeping the merged
/// runtime free of a standing in-memory dashboard client (Console Patterns Charter section 11). The
/// dashboard authoring surface stays bound to the real honua-server dashboard package lifecycle on the
/// publication registry (honua-server#1183) or shows this state.
/// </summary>
public sealed class UnsupportedStudioDashboardPackageDataSource : IStudioDashboardPackageDataSource
{
    private static readonly StudioDashboardCapabilityState MissingBinding = new(
        "Dashboard builder",
        "Missing binding",
        "Honua:Server:BaseUrl",
        "Configure Honua:Server:BaseUrl or HONUA_SERVER_BASE_URL so the dashboard builder can bind the server-owned dashboard package lifecycle from honua-server.");

    public Task<StudioDashboardWorkspace> GetWorkspaceAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new StudioDashboardWorkspace([], [MissingBinding]));

    public Task<StudioDashboardEditorLoad> LoadAsync(string? dashboardId, CancellationToken cancellationToken = default) =>
        Task.FromResult(new StudioDashboardEditorLoad(null, [MissingBinding]));

    public Task<StudioDashboardCommandResult> SaveDraftAsync(
        StudioDashboardEditorState state,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(BindingFailure());

    public Task<StudioDashboardCommandResult> ValidateAsync(
        StudioDashboardEditorState state,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(BindingFailure());

    public Task<StudioDashboardCommandResult> PublishAsync(
        StudioDashboardEditorState state,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(BindingFailure());

    public Task<StudioDashboardCommandResult> ReopenAsync(
        string dashboardId,
        int version,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(BindingFailure());

    private static StudioDashboardCommandResult BindingFailure() =>
        new(false, MissingBinding.Detail, Issue: MissingBinding);
}
