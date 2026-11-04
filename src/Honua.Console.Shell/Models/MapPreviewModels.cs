using System.Collections.Generic;

namespace Honua.Console.Shell.Models;

public enum MapPreviewMode
{
    Layer,

    Service,
}

public enum MapPreviewTimeFrame
{
    None,

    AsOf,

    Now,
}

public sealed record MapPreviewBasemap(string Id, string Label, string? StyleUrl = null);

public sealed record MapPreviewLegendEntry(string Label, string Color, string? Detail = null);

public sealed record MapPreviewLayer(int Id, string Name, string Color, bool Visible = true);

public sealed record MapPreviewPopupField(string Label, string Value, bool Muted = false);

public sealed record MapPreviewPopup(
    string Title,
    IReadOnlyList<MapPreviewPopupField> Fields,
    string? Footnote = null);
