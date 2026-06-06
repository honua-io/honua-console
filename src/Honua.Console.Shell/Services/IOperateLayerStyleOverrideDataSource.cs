using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Reads and writes the per-publication-slot style/popup OVERRIDES that back the Operate resource-presentation
/// style editor (<c>/operate/layers/{id}/style</c>, UI-032). This is the resource/slot presentation surface,
/// distinct from the Studio canonical style editor (<c>/studio/styles</c>). Per Console Patterns Charter
/// section 11, the merged build registers <see cref="UnsupportedOperateLayerStyleOverrideDataSource"/> (an
/// honest missing-binding state) because the per-slot override contract is not yet exposed by honua-server;
/// the page still reuses the REAL <c>/ogc/styles</c> list (via <see cref="IStudioMapStyleCatalogDataSource"/>)
/// for the available base styles. Each result carries an <see cref="OperateLayerStyleBindingState"/> so an
/// unbound or denied override read/write renders an explanation instead of fabricated overrides.
/// </summary>
public interface IOperateLayerStyleOverrideDataSource
{
    /// <summary>Lists the layer's publication slots and their style/popup overrides, or a binding state.</summary>
    Task<OperateLayerStyleOverrideView> GetOverridesAsync(
        string resourceId,
        CancellationToken cancellationToken = default);

    /// <summary>Saves a single slot's style/popup override; a blocked write carries a binding state.</summary>
    Task<OperateLayerStyleOverrideSaveResult> SaveOverrideAsync(
        OperateLayerSlotStyleOverrideEdit edit,
        CancellationToken cancellationToken = default);
}
