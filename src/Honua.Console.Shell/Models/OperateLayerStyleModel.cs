namespace Honua.Console.Shell.Models;

// View models for the Operate resource-presentation style editor
// (/operate/layers/{id}/style, UI-032): per-publication-slot style and popup OVERRIDES on a layer's
// exposures. Distinct from the Studio-scoped style editor (/studio/styles), which authors the
// canonical OGC API styles. The base style read reuses the real /ogc/styles list; the per-slot
// override read/write is server-owned and renders a missing-binding state until that contract lands
// (Console Patterns Charter section 11).

public sealed record OperateLayerStyleBindingState(string Surface, string State, string Contract, string Detail)
{
    public const string MissingBinding = "Missing binding";
    public const string Forbidden = "Forbidden";
    public const string Unsupported = "Unsupported";
}

public sealed record OperateLayerStyleOverrideView(
    string ResourceId,
    IReadOnlyList<OperateLayerStyleOverride> Layers,
    OperateLayerStyleBindingState? BindingState = null)
{
    public int? LayerId { get; init; }
    public string? CanonicalResourceId { get; init; }
}

// PopupInfoJson is the GeoServices popupInfo document and DrawingInfoJson is the drawingInfo renderer. Empty means nothing is stored.
public sealed record OperateLayerStyleOverride(
    string LayerId,
    string ServiceName,
    string ServiceDisplayName,
    string? StyleIdOverride = null,
    string? PopupTemplateOverride = null,
    string? PopupInfoJson = null,
    string? DrawingInfoJson = null);

// A blank PopupInfoJson or DrawingInfoJson clears the stored document. StyleIdOverride and PopupTemplateOverride are not sent to the server.
public sealed record OperateLayerStyleOverrideEdit(
    string ResourceId,
    string LayerId,
    string? StyleIdOverride = null,
    string? PopupTemplateOverride = null,
    string? PopupInfoJson = null,
    string? DrawingInfoJson = null);

public sealed record OperateLayerStyleOverrideSaveResult(
    bool Succeeded,
    OperateLayerStyleBindingState? BindingState = null)
{
    public static OperateLayerStyleOverrideSaveResult Blocked(OperateLayerStyleBindingState state) =>
        new(Succeeded: false, state);
}
