using System.Text.Json.Serialization;
using Honua.Sdk.Admin.Models;

namespace Honua.Console.Contracts;

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(PublishLayerRequest))]
internal sealed partial class PublishingJsonContext : JsonSerializerContext;
