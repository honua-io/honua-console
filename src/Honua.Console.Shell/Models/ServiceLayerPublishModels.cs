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

    /// <summary>
    /// The <c>ServiceProtocols</c> ids the resource is being exposed through on this service slot (e.g.
    /// <c>FeatureServer</c>, <c>MapServer</c>, <c>Stac</c>). The layer publish lands the layer once; these
    /// drive a single service-protocol enablement so each selected protocol is actually published rather than
    /// the same layer-publish being re-posted per protocol (over-reporting live publications).
    /// </summary>
    public IReadOnlyList<string> Protocols { get; init; } = [];
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

/// <summary>
/// Outcome of enabling a set of <c>ServiceProtocols</c> on a service slot. On success
/// <see cref="Succeeded"/> is <c>true</c> and <see cref="EnabledProtocols"/> is the canonical set the service
/// now exposes (so the flow reports only the protocols that are genuinely live). On failure (missing binding,
/// rejection, transport error) <see cref="Succeeded"/> is <c>false</c> and <see cref="State"/> /
/// <see cref="Detail"/> carry the neutral state vocabulary token and reason.
/// </summary>
public sealed record ServiceProtocolEnableResult
{
    public bool Succeeded { get; init; }

    /// <summary>State vocabulary token (e.g. "Published", "Missing binding", "Rejected", "Unavailable").</summary>
    public required string State { get; init; }

    public string? Detail { get; init; }

    /// <summary>The protocols the service exposes after the change (the canonical post-change set).</summary>
    public IReadOnlyList<string> EnabledProtocols { get; init; } = [];

    public static ServiceProtocolEnableResult MissingBinding(string detail) => new()
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

/// <summary>
/// A publishable (PostGIS spatial) table discovered on a connection, used to populate the publish-layer
/// table picker and prefill geometry/SRID/fields for the publish command.
/// </summary>
public sealed record ServiceLayerPublishTable
{
    public required string Schema { get; init; }

    public required string Table { get; init; }

    public string? GeometryColumn { get; init; }

    public string? GeometryType { get; init; }

    public int? Srid { get; init; }

    public long? EstimatedRows { get; init; }

    public IReadOnlyList<string> Columns { get; init; } = [];

    /// <summary>Schema-qualified name (e.g. <c>public.parcels</c>) for display and selection keys.</summary>
    public string QualifiedName => string.IsNullOrWhiteSpace(Schema) ? Table : $"{Schema}.{Table}";
}
