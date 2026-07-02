namespace Honua.Console.Shell.Models;

// View models for the Operate resource-presentation style editor
// (/operate/layers/{id}/style, UI-032): per-publication-slot style and popup OVERRIDES on a layer's
// exposures. Distinct from the Studio-scoped style editor (/studio/styles), which authors the
// canonical OGC API styles. The base style read reuses the real /ogc/styles list; the per-slot
// override read/write is server-owned and renders a missing-binding state until that contract lands
// (Console Patterns Charter section 11).

/// <summary>Neutral binding/capability state for the per-slot presentation override surface.</summary>
public sealed record OperateLayerStyleBindingState(string Surface, string State, string Contract, string Detail)
{
    public const string MissingBinding = "Missing binding";
    public const string Forbidden = "Forbidden";
    public const string Unsupported = "Unsupported";
}

/// <summary>
/// The per-slot presentation overrides for one layer plus any binding/capability state. A publication slot
/// is one service exposure of the layer; each slot may override the canonical style and the popup template.
/// </summary>
public sealed record OperateLayerStyleOverrideView(
    string ResourceId,
    IReadOnlyList<OperateLayerSlotStyleOverride> Slots,
    OperateLayerStyleBindingState? BindingState = null);

/// <summary>
/// One publication slot's presentation override. For the per-layer authoring surface backed by honua-server's
/// popup-info + drawing-info admin endpoints, a layer is presented as a single slot whose
/// <see cref="PopupInfoJson"/> is the stored GeoServices popupInfo template ({title, fieldInfos:[...]}) and
/// whose <see cref="DrawingInfoJson"/> is the stored drawingInfo renderer document ({renderer:{...}}). Both
/// are the raw server JSON, pretty-printed for editing; empty means nothing is authored. The legacy
/// <see cref="StyleIdOverride"/>/<see cref="PopupTemplateOverride"/> fields are retained for compatibility
/// with the missing-binding shell and are unused by the server-bound source.
/// </summary>
public sealed record OperateLayerSlotStyleOverride(
    string SlotId,
    string ServiceName,
    string ServiceDisplayName,
    string? StyleIdOverride = null,
    string? PopupTemplateOverride = null,
    string? PopupInfoJson = null,
    string? DrawingInfoJson = null);

/// <summary>
/// An override edit submitted by the editor for one slot. <see cref="PopupInfoJson"/> and
/// <see cref="DrawingInfoJson"/> carry the raw popupInfo / drawingInfo documents to persist (a blank value
/// clears the stored document).
/// </summary>
public sealed record OperateLayerSlotStyleOverrideEdit(
    string ResourceId,
    string SlotId,
    string? StyleIdOverride = null,
    string? PopupTemplateOverride = null,
    string? PopupInfoJson = null,
    string? DrawingInfoJson = null);

/// <summary>Result of a per-slot override save: a binding state when the write is blocked.</summary>
public sealed record OperateLayerStyleOverrideSaveResult(
    bool Succeeded,
    OperateLayerStyleBindingState? BindingState = null)
{
    public static OperateLayerStyleOverrideSaveResult Blocked(OperateLayerStyleBindingState state) =>
        new(Succeeded: false, state);
}
