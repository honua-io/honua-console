using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Honua.Console.Shell.Models;

// Console-side projections of the OGC SensorThings API (STA v1.1) read surface
// served by honua-server under /sta/v1.1 (PR #1842 / #1747). These records mirror
// the conformance-shaped wire contract (@iot.id / @iot.selfLink /
// @iot.navigationLink, `value` arrays, @iot.nextLink paging) so the console can
// browse Things/Datastreams/Observations and chart a datastream's time series
// without standing up a bespoke STA parser. The server module is the source of
// truth; these stay a thin read-only projection (the temporary Contracts-shim
// boundary the repo uses until honua-sdk-dotnet projects STA).

/// <summary>
/// STA entity-collection envelope: a <c>value</c> array plus optional
/// <c>@iot.count</c> and <c>@iot.nextLink</c> paging members.
/// </summary>
public sealed record StaEntitySet<T>
{
    [JsonPropertyName("@iot.count")]
    public long? Count { get; init; }

    [JsonPropertyName("value")]
    public IReadOnlyList<T> Value { get; init; } = [];

    [JsonPropertyName("@iot.nextLink")]
    public string? NextLink { get; init; }
}

/// <summary>STA v1.1 <c>Thing</c> entity.</summary>
public sealed record StaThing
{
    [JsonPropertyName("@iot.id")]
    public long IotId { get; init; }

    [JsonPropertyName("@iot.selfLink")]
    public string IotSelfLink { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    // STA `properties` is an open object whose values may be any JSON type (string, number,
    // boolean, nested object/array). Typing the values as string makes the source-generated
    // deserializer throw JsonException on any non-string value, which fails the whole collection
    // read (FetchAsync -> Denied). Keep the values as raw JsonElement so any conformant value
    // round-trips (mirrors the Observation.result JsonElement handling).
    [JsonPropertyName("properties")]
    public IReadOnlyDictionary<string, JsonElement>? Properties { get; init; }

    [JsonPropertyName("Datastreams@iot.navigationLink")]
    public string? DatastreamsNavigationLink { get; init; }
}

/// <summary>STA v1.1 unit-of-measurement object embedded in a Datastream.</summary>
public sealed record StaUnitOfMeasurement
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("symbol")]
    public string Symbol { get; init; } = string.Empty;

    [JsonPropertyName("definition")]
    public string Definition { get; init; } = string.Empty;
}

/// <summary>STA v1.1 <c>Datastream</c> entity.</summary>
public sealed record StaDatastream
{
    [JsonPropertyName("@iot.id")]
    public long IotId { get; init; }

    [JsonPropertyName("@iot.selfLink")]
    public string IotSelfLink { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    [JsonPropertyName("observationType")]
    public string ObservationType { get; init; } = string.Empty;

    [JsonPropertyName("unitOfMeasurement")]
    public StaUnitOfMeasurement? UnitOfMeasurement { get; init; }

    [JsonPropertyName("phenomenonTime")]
    public string? PhenomenonTime { get; init; }

    [JsonPropertyName("Observations@iot.navigationLink")]
    public string? ObservationsNavigationLink { get; init; }

    [JsonPropertyName("Thing@iot.navigationLink")]
    public string? ThingNavigationLink { get; init; }

    [JsonPropertyName("Sensor@iot.navigationLink")]
    public string? SensorNavigationLink { get; init; }

    [JsonPropertyName("ObservedProperty@iot.navigationLink")]
    public string? ObservedPropertyNavigationLink { get; init; }

    [JsonPropertyName("Observations")]
    public IReadOnlyList<StaObservation>? Observations { get; init; }

    [JsonPropertyName("Thing")]
    public StaThing? Thing { get; init; }
}

/// <summary>STA v1.1 <c>Observation</c> entity.</summary>
public sealed record StaObservation
{
    [JsonPropertyName("@iot.id")]
    public long IotId { get; init; }

    [JsonPropertyName("@iot.selfLink")]
    public string IotSelfLink { get; init; } = string.Empty;

    [JsonPropertyName("phenomenonTime")]
    public string PhenomenonTime { get; init; } = string.Empty;

    [JsonPropertyName("resultTime")]
    public string? ResultTime { get; init; }

    // OGC STA v1.1 leaves Observation.result an open type: a number, string, boolean, JSON
    // object/array, or null (category/truth datastreams, or a gap in an otherwise-numeric stream).
    // Modelling it as a raw JsonElement keeps deserialization total — a single non-numeric or null
    // result no longer throws JsonException and degrades the WHOLE collection to Unavailable.
    // Numeric consumers project through <see cref="NumericResult"/>.
    [JsonPropertyName("result")]
    public JsonElement Result { get; init; }

    [JsonPropertyName("Datastream@iot.navigationLink")]
    public string? DatastreamNavigationLink { get; init; }

    /// <summary>
    /// The observation result as a <see cref="double"/> when it is numeric (a JSON number, or a
    /// string that parses as an invariant number), otherwise <c>null</c> for non-numeric/null
    /// results that cannot be plotted on a numeric axis.
    /// </summary>
    [JsonIgnore]
    public double? NumericResult => Result.ValueKind switch
    {
        JsonValueKind.Number when Result.TryGetDouble(out var value) => value,
        JsonValueKind.String when double.TryParse(
            Result.GetString(),
            NumberStyles.Float | NumberStyles.AllowThousands,
            CultureInfo.InvariantCulture,
            out var value) => value,
        _ => null
    };

    /// <summary>
    /// Builds an <see cref="StaObservation"/> result payload from a numeric value, for the
    /// demo/in-memory shell and tests (the live client deserializes the wire value directly).
    /// </summary>
    public static JsonElement NumericResultValue(double value) =>
        JsonSerializer.SerializeToElement(value);
}

/// <summary>
/// Read-status vocabulary for the SensorThings surface, mirroring the Operate
/// section-status pattern so missing/forbidden/unsupported/unavailable all
/// degrade through one shared surface (per the Console exception constraint).
/// </summary>
public enum SensorThingsReadStatus
{
    Ok,
    Missing,
    Forbidden,
    Unsupported,
    Unavailable
}

/// <summary>Read envelope carrying a status that drives the shared empty/denied surfaces.</summary>
public sealed record SensorThingsReadResult<T>
{
    public SensorThingsReadStatus Status { get; init; }

    public T? Value { get; init; }

    public string Message { get; init; } = string.Empty;

    public bool IsOk => Status == SensorThingsReadStatus.Ok;

    public static SensorThingsReadResult<T> Ok(T value) =>
        new() { Status = SensorThingsReadStatus.Ok, Value = value };

    public static SensorThingsReadResult<T> Denied(SensorThingsReadStatus status, string message) =>
        new() { Status = status, Message = message };
}

/// <summary>Consistent titles/copy for non-ok SensorThings read states.</summary>
public static class SensorThingsPresentation
{
    public const string MissingBindingMessage =
        "Connect a honua-server environment (Honua:Server:BaseUrl) to browse SensorThings entities.";

    public static string Title(SensorThingsReadStatus status) => status switch
    {
        SensorThingsReadStatus.Missing => "Not found",
        SensorThingsReadStatus.Forbidden => "Permission required",
        SensorThingsReadStatus.Unsupported => "Unsupported by this server",
        _ => "Temporarily unavailable"
    };

    public static string FallbackMessage(SensorThingsReadStatus status) => status switch
    {
        SensorThingsReadStatus.Missing => "This server build does not expose the requested SensorThings entity.",
        SensorThingsReadStatus.Forbidden => "The active environment profile is not permitted to read SensorThings.",
        SensorThingsReadStatus.Unsupported => "The connected server does not advertise the SensorThings (STA v1.1) API.",
        _ => "The honua-server SensorThings API could not be reached. Retry once the environment is connected."
    };
}

/// <summary>A single charted point of a datastream time series (phenomenon time + numeric result).</summary>
public sealed record SensorThingsTimeSeriesPoint(DateTimeOffset PhenomenonTime, double Result);

/// <summary>A datastream's observations projected for time-series charting.</summary>
public sealed record SensorThingsTimeSeries(
    long DatastreamId,
    string DatastreamName,
    string UnitSymbol,
    IReadOnlyList<SensorThingsTimeSeriesPoint> Points)
{
    public bool HasPoints => Points.Count > 0;

    public double Minimum => HasPoints ? Points.Min(p => p.Result) : 0;

    public double Maximum => HasPoints ? Points.Max(p => p.Result) : 0;
}
