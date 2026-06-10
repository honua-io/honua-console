namespace Honua.Console.Shell.Models;

/// <summary>
/// A layer's permanent (server-enforced) query filter as read from honua-server. <see cref="Bound"/> is
/// false (with <see cref="Detail"/>) when no server is configured or the filter could not be read.
/// <see cref="Expression"/> is empty and <see cref="HasFilter"/> false when no filter is saved on the layer.
/// </summary>
public sealed record ConsoleLayerFilter
{
    public bool Bound { get; init; }

    public string? Detail { get; init; }

    public int LayerId { get; init; }

    /// <summary>True when the server has a permanent filter saved on this layer.</summary>
    public bool HasFilter { get; init; }

    public string Expression { get; init; } = string.Empty;

    /// <summary>"arcgis-sql", "cql2-text", or "cql2-json"; defaults to "arcgis-sql" when unset.</summary>
    public string Language { get; init; } = "arcgis-sql";

    public static ConsoleLayerFilter Unbound(string detail) => new() { Bound = false, Detail = detail };
}

/// <summary>Outcome of saving or clearing a layer's permanent filter.</summary>
public sealed record ConsoleSetLayerFilterResult
{
    public bool Succeeded { get; init; }

    public required string State { get; init; }

    public string? Detail { get; init; }

    public static ConsoleSetLayerFilterResult MissingBinding(string detail) => new()
    {
        Succeeded = false,
        State = "Missing binding",
        Detail = detail
    };
}
