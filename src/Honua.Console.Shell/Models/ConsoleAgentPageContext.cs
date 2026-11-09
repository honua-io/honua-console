using System.Text.Json.Serialization;

namespace Honua.Console.Shell.Models;

[JsonConverter(typeof(JsonStringEnumConverter<ConsoleAgentEntityKind>))]
public enum ConsoleAgentEntityKind { Folder, Service, Layer, Job, Approval, Item }

public sealed record ConsoleAgentEntity
{
    public required ConsoleAgentEntityKind Kind { get; init; }
    public required string Id { get; init; }
    public string? ServiceName { get; init; }
    public string? ResourceId { get; init; }

    public static ConsoleAgentEntity ForLayer(OperateServiceLayerProjection layer) => new()
    {
        Kind = ConsoleAgentEntityKind.Layer,
        Id = layer.LayerId.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ResourceId = layer.CanonicalResourceId
    };

    public static ConsoleAgentEntity ForService(string name) => new()
    {
        Kind = ConsoleAgentEntityKind.Service,
        Id = name,
        ServiceName = name
    };
}

public sealed record ConsoleAgentUnsavedSummary
{
    public int ChangedFieldCount { get; init; }
    public int PendingOperationCount { get; init; }
}

public record ConsoleAgentPageContext
{
    public string Route { get; init; } = "/";
    public IReadOnlyList<ConsoleAgentEntity> Entities { get; init; } = [];
    public IReadOnlyDictionary<string, string> Filters { get; init; } = new Dictionary<string, string>();
    public IReadOnlyList<ConsoleAgentEntity> Selection { get; init; } = [];
    public ConsoleAgentUnsavedSummary Unsaved { get; init; } = new();
}

public sealed record ConsoleAgentContextSnapshot : ConsoleAgentPageContext
{
    public long Generation { get; init; }
}
