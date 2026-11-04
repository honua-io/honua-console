using System.Text.Json.Serialization;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

// Source-generated metadata so trimming keeps these wire projections.
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
