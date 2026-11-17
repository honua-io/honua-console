using System.Text.Json.Serialization;

namespace Honua.Console.Contracts;

// Exports admin API (honua-server#8260) and object-storage Connections (honua-server#8266).
// Responses use the admin ApiResponse envelope ({ success, data, message }); a 404 or 501 means the
// connected server does not offer the surface yet.
public static class ExportAdminRoutes
{
    public const string Exports = "api/v1/admin/exports";

    public const string ObjectStorageSetup = "api/v1/admin/connections/object-storage/setup";

    public const string Connections = "api/v1/admin/connections";

    public const string ConnectionDraftTest = "api/v1/admin/connections/test";

    public static string Export(string exportId) => $"{Exports}/{Uri.EscapeDataString(exportId)}";

    public static string ObjectStorageSetupFor(string bucket, string prefix) =>
        $"{ObjectStorageSetup}?bucket={Uri.EscapeDataString(bucket)}&prefix={Uri.EscapeDataString(prefix)}";

    public static string ConnectionObjects(Guid connectionId, string prefix) =>
        $"{Connections}/{connectionId:D}/objects" +
        (string.IsNullOrEmpty(prefix) ? string.Empty : $"?prefix={Uri.EscapeDataString(prefix)}");
}

public sealed record ExportListResponse
{
    public IReadOnlyList<ExportResponse> Items { get; init; } = [];
}

public sealed record ExportResponse
{
    public string Id { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public ExportSourceDescriptor Source { get; init; } = new();

    // "running", "ready", "failed" or "unpublished".
    public string Status { get; init; } = string.Empty;

    public string? CurrentVersion { get; init; }

    public DateTimeOffset? UpdatedAt { get; init; }

    // Absolute signed-in URL of the collection document.
    public string? CollectionUrl { get; init; }

    // Absolute public-data-path URL of the collection document; null while the Export is signed-in only.
    public string? PublicUrl { get; init; }

    // Why the Export is not public (row filter, hidden fields, tenancy, public data path off).
    public string? NotPublicReason { get; init; }

    public string? JobId { get; init; }

    public string? Message { get; init; }

    public IReadOnlyList<ExportVersionResponse> Versions { get; init; } = [];
}

public sealed record ExportSourceDescriptor
{
    // "layer", "upload" or "object".
    public string Kind { get; init; } = string.Empty;

    public string? ServiceId { get; init; }

    public int? LayerId { get; init; }

    public string? Name { get; init; }
}

public sealed record ExportVersionResponse
{
    public string Version { get; init; } = string.Empty;

    public DateTimeOffset? CreatedAt { get; init; }

    public long? FeatureCount { get; init; }

    public IReadOnlyList<ExportArtifactResponse> Artifacts { get; init; } = [];
}

public sealed record ExportArtifactResponse
{
    // "geoparquet", "pmtiles", "cog" or "collection".
    public string Kind { get; init; } = string.Empty;

    public string Href { get; init; } = string.Empty;

    public long? Bytes { get; init; }
}

public sealed record ExportCreateRequest
{
    public ExportSourceDescriptor Source { get; init; } = new();

    // Optional; the server derives one from the source name when omitted.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ExportId { get; init; }
}

public sealed record ObjectStorageConnectionRequest
{
    public string Name { get; init; } = string.Empty;

    public string Kind { get; init; } = "object-storage";

    public string Provider { get; init; } = "aws-s3";

    public string Bucket { get; init; } = string.Empty;

    public string Prefix { get; init; } = string.Empty;

    public string Region { get; init; } = string.Empty;

    public string RoleArn { get; init; } = string.Empty;

    public string ExternalId { get; init; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; init; }
}

public sealed record ObjectStorageSetupResponse
{
    public string ExternalId { get; init; } = string.Empty;

    public string? QuickCreateUrl { get; init; }

    public string? RoleNamePrefix { get; init; }
}

public sealed record ObjectStorageConnectionResponse
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;
}

public sealed record ObjectStorageTestResponse
{
    public bool IsHealthy { get; init; }

    public string? Message { get; init; }
}

public sealed record ObjectStorageBrowseResponse
{
    public string Bucket { get; init; } = string.Empty;

    public string Prefix { get; init; } = string.Empty;

    public IReadOnlyList<string> Folders { get; init; } = [];

    public IReadOnlyList<ObjectStorageObjectResponse> Objects { get; init; } = [];

    public bool Truncated { get; init; }
}

public sealed record ObjectStorageObjectResponse
{
    public string Key { get; init; } = string.Empty;

    public long? Bytes { get; init; }

    // Detected from the file header: "cog", "geotiff", "pmtiles", "geoparquet" or "other".
    public string Kind { get; init; } = "other";

    public DateTimeOffset? LastModified { get; init; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(ConsoleApiEnvelope<ExportListResponse>), TypeInfoPropertyName = "ExportListEnvelope")]
[JsonSerializable(typeof(ConsoleApiEnvelope<ExportResponse>), TypeInfoPropertyName = "ExportEnvelope")]
[JsonSerializable(typeof(ConsoleApiEnvelope<ObjectStorageSetupResponse>), TypeInfoPropertyName = "ObjectStorageSetupEnvelope")]
[JsonSerializable(typeof(ConsoleApiEnvelope<ObjectStorageConnectionResponse>), TypeInfoPropertyName = "ObjectStorageConnectionEnvelope")]
[JsonSerializable(typeof(ConsoleApiEnvelope<ObjectStorageTestResponse>), TypeInfoPropertyName = "ObjectStorageTestEnvelope")]
[JsonSerializable(typeof(ConsoleApiEnvelope<ObjectStorageBrowseResponse>), TypeInfoPropertyName = "ObjectStorageBrowseEnvelope")]
[JsonSerializable(typeof(ExportCreateRequest))]
[JsonSerializable(typeof(ObjectStorageConnectionRequest))]
public sealed partial class ExportAdminJsonContext : JsonSerializerContext;
