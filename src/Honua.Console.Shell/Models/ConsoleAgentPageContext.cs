using System.Text.Json.Serialization;

namespace Honua.Console.Shell.Models;

[JsonConverter(typeof(JsonStringEnumConverter<ConsoleAgentEntityKind>))]
public enum ConsoleAgentEntityKind { Folder, Service, Layer, Job, Approval, Item, Connection }

public sealed record ConsoleAgentEntity
{
    public required ConsoleAgentEntityKind Kind { get; init; }
    public required string Id { get; init; }
    public string? ServiceName { get; init; }
    public string? ResourceId { get; init; }

    public static ConsoleAgentEntity ForLayer(OperateServiceLayer layer) => ForLayer(layer.LayerId, layer.CanonicalResourceId);

    public static ConsoleAgentEntity ForLayer(int layerId, string canonicalResourceId) => new()
    {
        Kind = ConsoleAgentEntityKind.Layer,
        Id = layerId.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ResourceId = canonicalResourceId
    };

    public static ConsoleAgentEntity ForConnection(string id) => new()
    {
        Kind = ConsoleAgentEntityKind.Connection,
        Id = id
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

    public static ConsoleAgentPageContext EditorContext(
        string navigationUri,
        int changedFieldCount,
        int pendingOperationCount,
        IReadOnlyDictionary<string, string>? filters = null) =>
        new()
        {
            Route = new Uri(navigationUri).AbsolutePath,
            Filters = filters ?? new Dictionary<string, string>(),
            Unsaved = new ConsoleAgentUnsavedSummary
            {
                ChangedFieldCount = changedFieldCount,
                PendingOperationCount = pendingOperationCount,
            },
        };
}

public sealed record ConsoleAgentContextSnapshot : ConsoleAgentPageContext
{
    public long Generation { get; init; }
}
