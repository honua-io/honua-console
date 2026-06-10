namespace Honua.Console.Shell.Models;

/// <summary>
/// A layer's display hints as read from honua-server (scale window, default visibility, display field,
/// queryable, hasZ/hasM). <see cref="Bound"/> is false (with <see cref="Detail"/>) when no server is
/// configured or the display metadata could not be read.
/// </summary>
public sealed record ConsoleLayerDisplay
{
    public bool Bound { get; init; }

    public string? Detail { get; init; }

    public int LayerId { get; init; }

    public double? MinScale { get; init; }

    public double? MaxScale { get; init; }

    public bool? DefaultVisibility { get; init; }

    public string? DisplayField { get; init; }

    public bool? Queryable { get; init; }

    public bool? HasZ { get; init; }

    public bool? HasM { get; init; }

    public static ConsoleLayerDisplay Unbound(string detail) => new() { Bound = false, Detail = detail };
}

/// <summary>
/// A layer's editor-tracking + edit-capability metadata as read from honua-server. <see cref="Bound"/> is
/// false (with <see cref="Detail"/>) when no server is configured or the editing metadata could not be read.
/// </summary>
public sealed record ConsoleLayerEditing
{
    public bool Bound { get; init; }

    public string? Detail { get; init; }

    public int LayerId { get; init; }

    public string? GlobalIdField { get; init; }

    public string? CreatorField { get; init; }

    public string? CreatedAtField { get; init; }

    public string? EditorField { get; init; }

    public string? UpdatedAtField { get; init; }

    public bool? CanModify { get; init; }

    public bool? SupportsAttachments { get; init; }

    public bool? SupportsRelatedRecords { get; init; }

    public static ConsoleLayerEditing Unbound(string detail) => new() { Bound = false, Detail = detail };
}

/// <summary>
/// A layer's spatial/CRS metadata as read from honua-server (supported-CRS list, storage CRS, coordinate
/// epoch; SRID/geometry for read-only context). <see cref="Bound"/> is false (with <see cref="Detail"/>) when
/// no server is configured or the spatial metadata could not be read.
/// </summary>
public sealed record ConsoleLayerSpatial
{
    public bool Bound { get; init; }

    public string? Detail { get; init; }

    public int LayerId { get; init; }

    public int? Srid { get; init; }

    public string? GeometryType { get; init; }

    public IReadOnlyList<string> SupportedCrs { get; init; } = [];

    public string? StorageCrs { get; init; }

    public double? StorageCrsCoordinateEpoch { get; init; }

    public static ConsoleLayerSpatial Unbound(string detail) => new() { Bound = false, Detail = detail };
}

/// <summary>Outcome of saving a layer's display / editing / spatial metadata section.</summary>
public sealed record ConsoleSetLayerMetadataResult
{
    public bool Succeeded { get; init; }

    public required string State { get; init; }

    public string? Detail { get; init; }

    public static ConsoleSetLayerMetadataResult MissingBinding(string detail) => new()
    {
        Succeeded = false,
        State = "Missing binding",
        Detail = detail
    };
}
