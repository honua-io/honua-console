using System.Text.Json.Serialization;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Source-generated JSON metadata for the SensorThings (STA v1.1) read
/// projections, so the console deserializes the conformance-shaped wire
/// envelopes trim/AOT-safely (mirroring the Operate observability client).
/// </summary>
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(StaEntitySet<StaThing>))]
[JsonSerializable(typeof(StaEntitySet<StaDatastream>))]
[JsonSerializable(typeof(StaEntitySet<StaObservation>))]
[JsonSerializable(typeof(StaThing))]
[JsonSerializable(typeof(StaDatastream))]
[JsonSerializable(typeof(StaObservation))]
internal sealed partial class SensorThingsJsonContext : JsonSerializerContext
{
}
