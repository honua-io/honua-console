using System.Collections.Generic;

namespace Honua.Console.Shell.Models;

/// <summary>
/// How the shared <c>MapPreview</c> component frames its render. Mirrors the design-handoff
/// <c>MapPreview</c> (docs/design-handoff/console-canvas/map-preview.jsx): a single selected
/// layer, or a composite of all layers in a service with a toggle list overlay.
/// </summary>
public enum MapPreviewMode
{
    /// <summary>Just the selected layer's features, with style + labels + popup.</summary>
    Layer,

    /// <summary>Composite of all layers in the service, with a layer-list overlay.</summary>
    Service,
}

/// <summary>
/// Optional temporal framing of a <c>MapPreview</c>, used by the GitOps/temporal sync surfaces
/// (docs/design-handoff/console-canvas/screens-gitops-temporal-sync.jsx) to label a render as a
/// historical "as-of" snapshot or the live "now" state.
/// </summary>
public enum MapPreviewTimeFrame
{
    /// <summary>No temporal badge.</summary>
    None,

    /// <summary>Historical snapshot at a point in time (renders an "as-of" badge).</summary>
    AsOf,

    /// <summary>Live current state (renders a "now" badge).</summary>
    Now,
}

/// <summary>
/// A basemap choice rendered as a selectable chip beneath the map preview.
/// </summary>
/// <param name="Id">Stable identifier emitted to the basemap-changed callback.</param>
/// <param name="Label">Human-readable chip label.</param>
/// <param name="StyleUrl">
/// Optional MapLibre style URL (or inline style document URL) applied to the live map when the chip
/// is selected. This is what gets handed to <c>map.setStyle(...)</c> — never the chip <see cref="Id"/>,
/// which is a stable identifier, not a fetchable style. When null/blank, selecting the chip updates the
/// active selection and raises the callback but does not swap the live map style (honua-console#211).
/// </param>
public sealed record MapPreviewBasemap(string Id, string Label, string? StyleUrl = null);

/// <summary>
/// A single class-break (or categorical) legend entry for the optional style legend.
/// </summary>
/// <param name="Label">Display label for the break (e.g. a use-code or a value range).</param>
/// <param name="Color">CSS colour swatch for the break.</param>
/// <param name="Detail">Optional secondary text (count, range bound, etc.).</param>
public sealed record MapPreviewLegendEntry(string Label, string Color, string? Detail = null);

/// <summary>
/// A layer row for the service-mode layer-list overlay.
/// </summary>
/// <param name="Id">Numeric layer id shown in the overlay (e.g. <c>0</c>).</param>
/// <param name="Name">Layer display name.</param>
/// <param name="Color">Swatch colour.</param>
/// <param name="Visible">Whether the layer is currently shown.</param>
public sealed record MapPreviewLayer(int Id, string Name, string Color, bool Visible = true);

/// <summary>
/// A single key/value row inside the optional feature popup.
/// </summary>
public sealed record MapPreviewPopupField(string Label, string Value, bool Muted = false);

/// <summary>
/// Content for the optional GetFeatureInfo-style popup over the highlighted feature.
/// </summary>
public sealed record MapPreviewPopup(
    string Title,
    IReadOnlyList<MapPreviewPopupField> Fields,
    string? Footnote = null);
