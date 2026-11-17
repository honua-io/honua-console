using System.Text.Json.Serialization;

namespace Honua.Console.Contracts;

// honua-server names the layer and its related layer by storage identifier.
public sealed record HonuaAdminLayerRelationships
{
    [JsonPropertyName("storageLayerId")]
    public int LayerId { get; init; }

    public IReadOnlyList<HonuaAdminLayerRelationship> Relationships { get; init; } = [];
}

public sealed record HonuaAdminLayerRelationship
{
    public string? Id { get; init; }

    public string? Name { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; init; }

    [JsonPropertyName("relatedStorageLayerId")]
    public int? RelatedLayerId { get; init; }

    // Role is origin or destination.
    public string? Role { get; init; }

    public string? Cardinality { get; init; }

    public string? OriginField { get; init; }

    public string? DestinationField { get; init; }

    public int? EsriRelationshipId { get; init; }

    // The server's composite flag is not nullable; a new row omits it and gets the default.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Composite { get; init; }
}

public sealed record HonuaAdminLayerRelationshipsUpdate
{
    public IReadOnlyList<HonuaAdminLayerRelationship> Relationships { get; init; } = [];
}
