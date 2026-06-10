using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Missing-binding fallback for <see cref="IOperateLayerStyleOverrideDataSource"/>. When no honua-server base
/// URL is configured there is nothing to bind the per-layer popup-info / drawing-info authoring to, so the
/// merged build registers this source instead of <see cref="ServerOperateLayerStyleOverrideDataSource"/>: the
/// override read and write return an explicit missing-binding state and never fabricate a popup/renderer
/// (Console Patterns Charter section 11). The style editor page still reads the REAL /ogc/styles list through
/// <see cref="IStudioMapStyleCatalogDataSource"/>; only the override portion is unbound. Mirrors the
/// Server*/Unsupported* pairing of the other Operate data sources.
/// </summary>
public sealed class UnsupportedOperateLayerStyleOverrideDataSource : IOperateLayerStyleOverrideDataSource
{
    internal const string Surface = "Resource presentation overrides";

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
        OperateLayerSlotStyleOverrideEdit edit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edit);
        return Task.FromResult(OperateLayerStyleOverrideSaveResult.Blocked(MissingBinding));
    }
}
