using System.Text.Json.Serialization;

namespace Honua.Console.Shell.Models;

// Console-side projections of the honua-server scene surface: the public scene
// discovery list (GET /api/scenes) and the admin point-cloud ingest result
// (POST /api/v1/admin/scenes/ingest/pointcloud), both shipped in PR #1840
// (#1201). The resulting tilesets are served by the existing scene routes
// (/scenes/{id}/tileset.json + /scenes/{id}/{*assetPath}). These records mirror
// the server wire contract so the console can discover renderable point-cloud
// scenes and view them in a 3D Tiles viewer.

public sealed record SceneBounds
{
    [JsonPropertyName("west")]
    public double West { get; init; }

    [JsonPropertyName("south")]
    public double South { get; init; }

    [JsonPropertyName("east")]
    public double East { get; init; }

    [JsonPropertyName("north")]
    public double North { get; init; }
}

public sealed record SceneSummary
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("tilesetUrl")]
    public string? TilesetUrl { get; init; }

    [JsonPropertyName("bounds")]
    public SceneBounds? Bounds { get; init; }

    [JsonPropertyName("capabilities")]
    public IReadOnlyList<string> Capabilities { get; init; } = [];

    [JsonPropertyName("attribution")]
    public IReadOnlyList<string> Attribution { get; init; } = [];

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset? UpdatedAt { get; init; }
}

public sealed record SceneListResponse
{
    [JsonPropertyName("scenes")]
    public IReadOnlyList<SceneSummary> Scenes { get; init; } = [];
}

public sealed record PointCloudIngestResult
{
    [JsonPropertyName("sceneId")]
    public string SceneId { get; init; } = string.Empty;

    [JsonPropertyName("tilesetUrl")]
    public string TilesetUrl { get; init; } = string.Empty;

    [JsonPropertyName("pointCount")]
    public int PointCount { get; init; }

    [JsonPropertyName("tileCount")]
    public int TileCount { get; init; }

    [JsonPropertyName("hasColor")]
    public bool HasColor { get; init; }

    [JsonPropertyName("boundingRegionDegrees")]
    public IReadOnlyList<double> BoundingRegionDegrees { get; init; } = [];
}

public enum SceneReadStatus
{
    Ok,
    Missing,
    Forbidden,
    Unsupported,
    PaymentRequired,
    Conflict,
    InvalidInput,
    Unavailable
}

public sealed record SceneReadResult<T>
{
    public SceneReadStatus Status { get; init; }

    public T? Value { get; init; }

    public string Message { get; init; } = string.Empty;

    public bool IsOk => Status == SceneReadStatus.Ok;

    public static SceneReadResult<T> Ok(T value) =>
        new() { Status = SceneReadStatus.Ok, Value = value };

    public static SceneReadResult<T> Denied(SceneReadStatus status, string message) =>
        new() { Status = status, Message = message };
}

public sealed record PointCloudIngestRequest
{
    public required byte[] Content { get; init; }

    public required string FileName { get; init; }

    public string? DisplayName { get; init; }

    public string? Description { get; init; }

    public int? SourceCrs { get; init; }
}

public static class ScenePresentation
{
    public const string MissingBindingMessage =
        "Connect a honua-server environment (Honua:Server:BaseUrl) to discover and view 3D scenes.";

    public static string Title(SceneReadStatus status) => status switch
    {
        SceneReadStatus.Missing => "Not found",
        SceneReadStatus.Forbidden => "Permission required",
        SceneReadStatus.Unsupported => "Unsupported by this server",
        SceneReadStatus.PaymentRequired => "Enterprise feature",
        SceneReadStatus.Conflict => "Conflict",
        SceneReadStatus.InvalidInput => "Invalid input",
        _ => "Temporarily unavailable"
    };

    public static string FallbackMessage(SceneReadStatus status) => status switch
    {
        SceneReadStatus.Missing => "The requested scene was not found on this server build.",
        SceneReadStatus.Forbidden => "The active environment profile is not permitted to read scenes.",
        SceneReadStatus.Unsupported => "The connected server does not advertise scene serving.",
        SceneReadStatus.PaymentRequired => "Point-cloud ingest requires an Enterprise edition entitlement on the connected server.",
        SceneReadStatus.Conflict => "A scene with this identifier already exists on the server.",
        SceneReadStatus.InvalidInput => "The uploaded LAS point cloud was rejected by the server.",
        _ => "The honua-server scene API could not be reached. Retry once the environment is connected."
    };
}
