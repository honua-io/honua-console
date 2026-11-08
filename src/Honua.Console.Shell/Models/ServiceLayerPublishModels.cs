using Honua.Sdk.Admin.Models;

namespace Honua.Console.Shell.Models;

public sealed record ServiceLayerPublishCommand
{
    public OptionalString FolderId { get; init; }
    public string? PublicName { get; init; }
    public string? AuthorityBinding { get; init; }
    public required string ConnectionId { get; init; }

    public required string Schema { get; init; }

    public required string Table { get; init; }

    public required string LayerName { get; init; }

    public required string ServiceName { get; init; }

    public string? Description { get; init; }

    public string? GeometryColumn { get; init; }

    public string? GeometryType { get; init; }

    public int? Srid { get; init; }

    public string? PrimaryKey { get; init; }

    public IReadOnlyList<string> Fields { get; init; } = [];

    public bool Enabled { get; init; } = true;
}

public sealed record ServiceLayerPublishResult
{
    public bool Succeeded { get; init; }

    public required string State { get; init; }

    public string? Detail { get; init; }

    public int? LayerId { get; init; }

    public string? LayerName { get; init; }

    public string? ServiceName { get; init; }

    public string? GeometryType { get; init; }

    public int? Srid { get; init; }

    public bool? Enabled { get; init; }

    public IReadOnlyList<ServiceLayerPublishFieldError> FieldErrors { get; init; } = [];

    public static ServiceLayerPublishResult MissingBinding(string detail) => new()
    {
        Succeeded = false,
        State = "Missing binding",
        Detail = detail
    };
}

public sealed record ServiceLayerPublishFieldError(
    string Code,
    string Message,
    string? Path = null,
    string? FieldId = null,
    string? Severity = null);

public sealed record ServiceLayerPublishTable
{
    public required string Schema { get; init; }

    public required string Table { get; init; }

    public string? GeometryColumn { get; init; }

    public string? GeometryType { get; init; }

    public int? Srid { get; init; }

    public long? EstimatedRows { get; init; }

    public IReadOnlyList<string> Columns { get; init; } = [];

    public string QualifiedName => string.IsNullOrWhiteSpace(Schema) ? Table : $"{Schema}.{Table}";
}
