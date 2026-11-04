namespace Honua.Console.Shell.Models;

public sealed record ConsoleLayerFilter
{
    public bool Bound { get; init; }

    public string? Detail { get; init; }

    public int LayerId { get; init; }

    public bool HasFilter { get; init; }

    public string Expression { get; init; } = string.Empty;

    // One of arcgis-sql, cql2-text, or cql2-json.
    public string Language { get; init; } = "arcgis-sql";

    public static ConsoleLayerFilter Unbound(string detail) => new() { Bound = false, Detail = detail };
}

public sealed record ConsoleSetLayerFilterResult : ConsoleOperationResult<ConsoleSetLayerFilterResult>;
