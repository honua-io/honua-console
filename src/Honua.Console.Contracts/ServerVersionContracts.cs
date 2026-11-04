using System.Text.Json.Serialization;

namespace Honua.Console.Contracts;

public static class ServerVersionAdminRoutes
{
    public const string Prefix = "api/v1/capabilities";

    public static string Manifest() => $"{Prefix}/manifest";
}

public sealed record CapabilityManifestServerInfoResponse
{
    [JsonPropertyName("serverVersion")]
    public string ServerVersion { get; init; } = string.Empty;

    [JsonPropertyName("apiVersion")]
    public string ApiVersion { get; init; } = string.Empty;

    [JsonPropertyName("metadataApiVersion")]
    public string MetadataApiVersion { get; init; } = string.Empty;

    [JsonPropertyName("metadataSchemaVersion")]
    public string MetadataSchemaVersion { get; init; } = string.Empty;

    [JsonPropertyName("deploymentEnvironment")]
    public string DeploymentEnvironment { get; init; } = string.Empty;
}

public sealed record CapabilityManifestServerEnvelope
{
    [JsonPropertyName("issuedAt")]
    public DateTimeOffset IssuedAt { get; init; }

    [JsonPropertyName("server")]
    public CapabilityManifestServerInfoResponse? Server { get; init; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(CapabilityManifestServerEnvelope))]
public sealed partial class ServerVersionJsonContext : JsonSerializerContext
{
}
