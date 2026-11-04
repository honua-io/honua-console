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

public sealed record StaEntitySet<T>
{
    [JsonPropertyName("@iot.count")]
    public long? Count { get; init; }

    [JsonPropertyName("value")]
    public IReadOnlyList<T> Value { get; init; } = [];

    [JsonPropertyName("@iot.nextLink")]
    public string? NextLink { get; init; }
}

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

public sealed record StaUnitOfMeasurement
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("symbol")]
    public string Symbol { get; init; } = string.Empty;

    [JsonPropertyName("definition")]
    public string Definition { get; init; } = string.Empty;
}

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

    // OGC STA v1.1 leaves Observation.result an open type, so one non-numeric or null result must not fail the collection.
    [JsonPropertyName("result")]
    public JsonElement Result { get; init; }

    [JsonPropertyName("Datastream@iot.navigationLink")]
    public string? DatastreamNavigationLink { get; init; }

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

    public static JsonElement NumericResultValue(double value) =>
        JsonSerializer.SerializeToElement(value);
}

public enum SensorThingsReadStatus
{
    Ok,
    Missing,
    Forbidden,
    Unsupported,
    Unavailable
}

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

public sealed record SensorThingsTimeSeriesPoint(DateTimeOffset PhenomenonTime, double Result);

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
