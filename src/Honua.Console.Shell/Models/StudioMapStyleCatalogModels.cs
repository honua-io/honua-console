namespace Honua.Console.Shell.Models;

/// <summary>One server-advertised styleId option for the per-layer style picker (ADR-0048).</summary>
public sealed record StudioMapStyleOption(string StyleId, string? Title)
{
    /// <summary>Picker label: the human title when present, else the bare styleId.</summary>
    public string Label => string.IsNullOrWhiteSpace(Title) ? StyleId : Title!;
}

/// <summary>
/// The server-advertised style catalog backing the per-layer style picker, plus any
/// binding/permission capability state when the OGC API - Styles list (<c>GET /ogc/styles</c>, ADR-0048)
/// cannot be read. When <see cref="Issue"/> is set the picker degrades to a free-form style string
/// rather than fabricating a style list.
/// </summary>
public sealed record StudioMapStyleCatalog(
    IReadOnlyList<StudioMapStyleOption> Styles,
    string? DefaultStyleId,
    StudioMapCapabilityState? Issue)
{
    /// <summary>An empty catalog with no issue — the styles list is simply empty server-side.</summary>
    public static StudioMapStyleCatalog Empty { get; } = new([], null, null);

    public bool HasStyles => Styles.Count > 0;

    /// <summary>True when the styleId is one the server advertises (case-insensitive on the stable id).</summary>
    public bool IsKnown(string? styleId) =>
        !string.IsNullOrWhiteSpace(styleId)
        && Styles.Any(option => string.Equals(option.StyleId, styleId, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// A binding/permission/empty surface for the style catalog, mirroring the Operate capability-state
/// pattern so missing bindings, missing permissions, and unsupported contracts render consistently.
/// </summary>
public sealed record StudioMapCapabilityState(
    string Surface,
    string State,
    string Contract,
    string Detail,
    string? Summary = null,
    int? StatusCode = null,
    string? IssueRef = null);
