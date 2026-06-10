using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Reads and writes the per-layer presentation OVERRIDES (GeoServices popupInfo + drawingInfo renderer) that
/// back the Operate resource-presentation style editor (<c>/operate/layers/{id}/style</c>, UI-032). This is the
/// resource presentation surface, distinct from the Studio canonical style editor (<c>/studio/styles</c>). When
/// a server base URL is configured the merged build registers
/// <see cref="ServerOperateLayerStyleOverrideDataSource"/>, which round-trips both documents through the shipped
/// admin authoring endpoints (<c>GET/PUT /api/v1/admin/metadata/layers/{id}/popup-info</c> and
/// <c>.../drawing-info</c>); otherwise it registers <see cref="UnsupportedOperateLayerStyleOverrideDataSource"/>
/// (an honest missing-binding state) per Console Patterns Charter section 11. Either way the page still reuses
/// the REAL <c>/ogc/styles</c> list (via <see cref="IStudioMapStyleCatalogDataSource"/>) for the available base
/// styles. Each result carries an <see cref="OperateLayerStyleBindingState"/> so an unbound or denied override
/// read/write renders an explanation instead of fabricated overrides.
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
