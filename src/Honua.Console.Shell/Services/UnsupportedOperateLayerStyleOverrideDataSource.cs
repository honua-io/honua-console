using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public sealed class UnsupportedOperateLayerStyleOverrideDataSource : IOperateLayerStyleOverrideDataSource
{
    internal const string Surface = "Layer presentation overrides";

    // Per-layer popup-info + drawing-info ride the shipped admin authoring endpoints.
    internal const string Contract = "GET/PUT /api/v1/admin/metadata/layers/{id}/popup-info|drawing-info";

    internal const string Detail =
        "Per-layer popup info and drawing info (renderer) bind to honua-server's admin authoring endpoints. "
        + "No server is configured, so there is nothing to author against. Configure Honua:Server:BaseUrl (or "
        + "HONUA_SERVER_BASE_URL) to connect a server. The available base styles below come from the live "
        + "/ogc/styles list; Console will not fabricate a popup or renderer.";

    private static readonly OperateLayerStyleBindingState MissingBinding =
        new(Surface, OperateLayerStyleBindingState.MissingBinding, Contract, Detail);

    public Task<OperateLayerStyleOverrideView> GetOverridesAsync(
        string resourceId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new OperateLayerStyleOverrideView(resourceId, [], MissingBinding));

    public Task<OperateLayerStyleOverrideSaveResult> SaveOverrideAsync(
        OperateLayerStyleOverrideEdit edit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edit);
        return Task.FromResult(OperateLayerStyleOverrideSaveResult.Blocked(MissingBinding));
    }
}
