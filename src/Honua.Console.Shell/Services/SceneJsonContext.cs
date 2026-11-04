using System.Text.Json.Serialization;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(SceneListResponse))]
[JsonSerializable(typeof(SceneSummary))]
[JsonSerializable(typeof(PointCloudIngestResult))]
internal sealed partial class SceneJsonContext : JsonSerializerContext
{
}
