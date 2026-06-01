namespace Honua.Console.Shell.Models;

/// <summary>
/// Operator intent for the service-layer-publish operation: which connection + PostGIS table to publish,
/// the layer identity, geometry/SRID/primary-key, the selected output fields, the target service slot, and
/// whether to enable the layer immediately. Mirrors the inputs the publishing wizard collects.
/// </summary>
public sealed record ServiceLayerPublishCommand
{
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

/// <summary>
/// Outcome of the service-layer-publish operation. On success, <see cref="Succeeded"/> is <c>true</c> and
/// <see cref="LayerId"/> / <see cref="ServiceName"/> identify the landed layer. On failure (missing
/// binding, validation rejection, transport error) <see cref="Succeeded"/> is <c>false</c>, <see cref="State"/>
/// carries the neutral state vocabulary token, and <see cref="FieldErrors"/> carries any field-addressable
/// validation errors the operator can bind onto the offending inputs.
/// </summary>
public sealed record ServiceLayerPublishResult
{
    public bool Succeeded { get; init; }

    /// <summary>State vocabulary token (e.g. "Published", "Missing binding", "Rejected", "Unavailable").</summary>
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

/// <summary>A field-addressable validation error surfaced by the publish operation.</summary>
public sealed record ServiceLayerPublishFieldError(
    string Code,
    string Message,
    string? Path = null,
    string? FieldId = null,
    string? Severity = null);
