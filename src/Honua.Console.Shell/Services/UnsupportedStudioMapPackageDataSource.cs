using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Map-builder data source used when no honua-server base address is configured. Every surface renders
/// an explicit missing-binding state instead of fabricating map data, keeping the merged runtime free of
/// a standing in-memory map client (Console Patterns Charter section 11). The map authoring surface stays
/// bound to the real honua-server map package lifecycle (honua-server#1180) and publication registry
/// (honua-server#1183) or shows this state.
/// </summary>
public sealed class UnsupportedStudioMapPackageDataSource : IStudioMapPackageDataSource
{
    private static readonly StudioMapCapabilityState MissingBinding = new(
        "Map builder",
        "Missing binding",
        "Honua:Server:BaseUrl",
        "Configure Honua:Server:BaseUrl or HONUA_SERVER_BASE_URL so the map builder can bind the server-owned map package lifecycle and publication registry from honua-server.");

    public Task<StudioMapWorkspace> GetWorkspaceAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new StudioMapWorkspace([], [MissingBinding]));

    public Task<StudioMapEditorLoad> LoadAsync(string? mapId, CancellationToken cancellationToken = default) =>
        Task.FromResult(new StudioMapEditorLoad(null, [MissingBinding]));

    public Task<StudioMapCommandResult> SaveDraftAsync(
        StudioMapEditorState state,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(BindingFailure());

    public Task<StudioMapCommandResult> PublishAsync(
        StudioMapEditorState state,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(BindingFailure());

    public Task<StudioMapCommandResult> ReopenAsync(
        string mapId,
        int version,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(BindingFailure());

    private static StudioMapCommandResult BindingFailure() =>
        new(false, MissingBinding.Detail, Issue: MissingBinding);
}
