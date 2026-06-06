namespace Honua.Console.Shell.Models;

/// <summary>
/// Route + view models for the Operate resource-presentation style editor
/// (<c>/operate/layers/{id}/style</c>, UI-032): per-publication-slot style and popup OVERRIDES on a layer's
/// exposures. Distinct from the Studio-scoped style editor (<c>/studio/styles</c>), which authors the
/// canonical OGC API styles. The base style read reuses the real <c>/ogc/styles</c> list; the per-slot
/// override read/write is server-owned and renders a missing-binding state until that contract lands
/// (Console Patterns Charter section 11).
/// </summary>
public static class OperateLayerStyleRoutes
{
    public static string Style(string resourceId) => $"/operate/layers/{Uri.EscapeDataString(resourceId)}/style";
}

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

/// <summary>One publication slot's style/popup override (server-owned shape once the contract lands).</summary>
public sealed record OperateLayerSlotStyleOverride(
    string SlotId,
    string ServiceName,
    string ServiceDisplayName,
    string? StyleIdOverride,
    string? PopupTemplateOverride);

/// <summary>An override edit submitted by the editor for one slot.</summary>
public sealed record OperateLayerSlotStyleOverrideEdit(
    string ResourceId,
    string SlotId,
    string? StyleIdOverride,
    string? PopupTemplateOverride);

/// <summary>Result of a per-slot override save: a binding state when the write is blocked.</summary>
public sealed record OperateLayerStyleOverrideSaveResult(
    bool Succeeded,
    OperateLayerStyleBindingState? BindingState = null)
{
    public static OperateLayerStyleOverrideSaveResult Blocked(OperateLayerStyleBindingState state) =>
        new(Succeeded: false, state);
}
