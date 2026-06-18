using System.Text.Json.Serialization;

namespace Honua.Console.Contracts;

// SHIM(honua-sdk-dotnet): The honua-server capability-manifest endpoint
// (GET /api/v1/capabilities/manifest, CapabilityManifestEndpoints) is not yet
// projected to honua-sdk-dotnet. The Console server-upgrade flow needs ONLY the
// server-version block of that manifest to detect the running container/build
// version of a target environment (and gate an upgrade when the target is on an
// OLDER Honua version). These records mirror just the `server` object of the
// manifest wire shape — not the full capability document — and are consumed
// through the single Console shim boundary until the SDK projection lands.
//
// Route map (concrete v1), anonymous (no admin key required):
//   GET /api/v1/capabilities/manifest -> CapabilityManifestServerEnvelope (server block)
//
// JSON on the wire is camelCase; the read is no-store. Only the server-version
// fields are bound; unknown manifest fields are ignored.

/// <summary>
/// Concrete v1 route for the honua-server capability manifest, kept in one place so the
/// client and tests share the exact server path.
/// </summary>
public static class ServerVersionAdminRoutes
{
    public const string Prefix = "api/v1/capabilities";

    public static string Manifest() => $"{Prefix}/manifest";
}

/// <summary>
/// The <c>server</c> block of the capability manifest: the running honua-server's
/// version, API version, metadata API/schema versions, and deployment environment.
/// Console binds only this block to detect a target environment's current Honua
/// version for the server-upgrade flow.
/// </summary>
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

/// <summary>
/// Minimal envelope over the capability manifest carrying only the <c>server</c> and
/// <c>issuedAt</c> fields the server-upgrade flow reads. Unknown manifest fields are
/// ignored by the deserializer.
/// </summary>
public sealed record CapabilityManifestServerEnvelope
{
    [JsonPropertyName("issuedAt")]
    public DateTimeOffset IssuedAt { get; init; }

    [JsonPropertyName("server")]
    public CapabilityManifestServerInfoResponse? Server { get; init; }
}

/// <summary>
/// Source-generated JSON context for the capability-manifest server-version read shape
/// (trim/AOT safe), mirroring the other Console contract contexts.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(CapabilityManifestServerEnvelope))]
public sealed partial class ServerVersionJsonContext : JsonSerializerContext
{
}
