namespace Honua.Console.Shell.Models;

/// <summary>An RGB color (0–255 per channel) used by the console's 3D symbology authoring surface.</summary>
public sealed record ConsoleRgbColor
{
    public int Red { get; init; }

    public int Green { get; init; }

    public int Blue { get; init; }
}

/// <summary>The console's view of a layer's 3D extrusion settings (height/base-height field, unit, default, hint).</summary>
public sealed record ConsoleLayerExtrusionSettings
{
    public string? HeightField { get; init; }

    public string? BaseHeightField { get; init; }

    /// <summary>"meters", "feet", or "usSurveyFeet".</summary>
    public string? Unit { get; init; }

    public double? DefaultHeight { get; init; }

    public string? MaterialHint { get; init; }
}

/// <summary>The console's view of one attribute-driven 3D symbology rule.</summary>
public sealed record ConsoleSymbology3DRule
{
    public string? Attribute { get; init; }

    /// <summary>"equals", "notEquals", "greaterThan", "greaterThanOrEqual", "lessThan", or "lessThanOrEqual".</summary>
    public string? Comparison { get; init; }

    /// <summary>The compared value, carried as its text form (the console authors scalar values as text).</summary>
    public string? Value { get; init; }

    public ConsoleRgbColor? Color { get; init; }

    public double? Opacity { get; init; }

    public bool? Visible { get; init; }
}

/// <summary>The console's view of a layer's 3D symbology (default color + opacity and the ordered rules).</summary>
public sealed record ConsoleSymbology3D
{
    public ConsoleRgbColor? DefaultColor { get; init; }

    public double? DefaultOpacity { get; init; }

    public IReadOnlyList<ConsoleSymbology3DRule> Rules { get; init; } = [];
}

/// <summary>
/// A layer's 3D extrusion + 3D symbology metadata as read from honua-server. <see cref="Bound"/> is false (with
/// <see cref="Detail"/>) when no server is configured or the metadata could not be read.
/// </summary>
public sealed record ConsoleLayer3D
{
    public bool Bound { get; init; }

    public string? Detail { get; init; }

    public int LayerId { get; init; }

    public ConsoleLayerExtrusionSettings? Extrusion { get; init; }

    public ConsoleSymbology3D? Symbology3D { get; init; }

    public static ConsoleLayer3D Unbound(string detail) => new() { Bound = false, Detail = detail };
}

/// <summary>
/// A layer's lifecycle status (lifecycle stage + operational state) as read from honua-server.
/// <see cref="Bound"/> is false (with <see cref="Detail"/>) when no server is configured or it could not be read.
/// </summary>
public sealed record ConsoleLayerStatus
{
    public bool Bound { get; init; }

    public string? Detail { get; init; }

    public int LayerId { get; init; }

    /// <summary>"draft", "active", "deprecated", "retired", or "archived".</summary>
    public string? Lifecycle { get; init; }

    /// <summary>"unknown", "ready", "pending", "degraded", or "failed".</summary>
    public string? State { get; init; }

    public static ConsoleLayerStatus Unbound(string detail) => new() { Bound = false, Detail = detail };
}
