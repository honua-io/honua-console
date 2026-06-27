namespace Honua.Console.Shell.Models;

/// <summary>
/// A layer's relationships as read from honua-server (origin/destination role, cardinality, join fields,
/// Esri relationship id). <see cref="Bound"/> is false (with <see cref="Detail"/>) when no server is
/// configured or the relationships could not be read.
/// </summary>
public sealed record ConsoleLayerRelationships
{
    public bool Bound { get; init; }

    public string? Detail { get; init; }

    public int LayerId { get; init; }

    public IReadOnlyList<ConsoleLayerRelationship> Relationships { get; init; } = [];

    public static ConsoleLayerRelationships Unbound(string detail) => new() { Bound = false, Detail = detail };
}

/// <summary>One layer relationship row.</summary>
public sealed record ConsoleLayerRelationship
{
    public string? Id { get; init; }

    public string? Name { get; init; }

    public int? RelatedLayerId { get; init; }

    /// <summary>"origin" or "destination".</summary>
    public string? Role { get; init; }

    /// <summary>e.g. "one-to-many".</summary>
    public string? Cardinality { get; init; }

    public string? OriginField { get; init; }

    public string? DestinationField { get; init; }

    public int? EsriRelationshipId { get; init; }
}

/// <summary>Outcome of replacing a layer's relationship set.</summary>
public sealed record ConsoleSetRelationshipsResult : ConsoleOperationResult<ConsoleSetRelationshipsResult>;
