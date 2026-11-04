namespace Honua.Console.Shell.Models;

public sealed record ConsoleLayerRelationships
{
    public bool Bound { get; init; }

    public string? Detail { get; init; }

    public int LayerId { get; init; }

    public IReadOnlyList<ConsoleLayerRelationship> Relationships { get; init; } = [];

    public static ConsoleLayerRelationships Unbound(string detail) => new() { Bound = false, Detail = detail };
}

public sealed record ConsoleLayerRelationship
{
    public string? Id { get; init; }

    public string? Name { get; init; }

    public int? RelatedLayerId { get; init; }

    public string? Role { get; init; }

    public string? Cardinality { get; init; }

    public string? OriginField { get; init; }

    public string? DestinationField { get; init; }

    public int? EsriRelationshipId { get; init; }
}

public sealed record ConsoleSetRelationshipsResult : ConsoleOperationResult<ConsoleSetRelationshipsResult>;
