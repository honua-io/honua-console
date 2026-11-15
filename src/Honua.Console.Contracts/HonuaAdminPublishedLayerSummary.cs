using System.Text.Json.Serialization;

namespace Honua.Console.Contracts;

// SHIM(honua-sdk-dotnet#166): Preserve current and pinned-server published-layer identities until SDK migration.
public sealed record HonuaAdminPublishedLayerSummary
{
    private int? _storageLayerId;
    private int? _legacyLayerId;

    [JsonPropertyName("storageLayerId")]
    public int LayerId
    {
        get => _storageLayerId ?? _legacyLayerId ?? 0;
        init => _storageLayerId = value;
    }

    [JsonInclude]
    [JsonPropertyName("layerId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    private int? LegacyLayerId
    {
        get => null;
        init => _legacyLayerId = value;
    }

    public string? LayerName { get; init; }

    public string? Schema { get; init; }

    public string? Table { get; init; }

    public string? Description { get; init; }

    public string? GeometryType { get; init; }

    public int? Srid { get; init; }

    public string? PrimaryKey { get; init; }

    public int? FieldCount { get; init; }

    public bool? Enabled { get; init; }

    public string? ServiceName { get; init; }

    // Stored extent in EPSG:4326, or null when the layer has none.
    public HonuaAdminLayerExtent? Extent { get; init; }
}

