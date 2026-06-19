using System.Text.Json.Serialization;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Source-generated JSON metadata for the scene discovery + point-cloud ingest
/// projections, so the console deserializes the server wire envelopes
/// trim/AOT-safely (mirroring the Operate observability client).
/// </summary>
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(SceneListResponse))]
[JsonSerializable(typeof(SceneSummary))]
[JsonSerializable(typeof(PointCloudIngestResult))]
internal sealed partial class SceneJsonContext : JsonSerializerContext
{
}
