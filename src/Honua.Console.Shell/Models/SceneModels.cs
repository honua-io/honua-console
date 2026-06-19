using System.Text.Json.Serialization;

namespace Honua.Console.Shell.Models;

// Console-side projections of the honua-server scene surface: the public scene
// discovery list (GET /api/scenes) and the admin point-cloud ingest result
// (POST /api/v1/admin/scenes/ingest/pointcloud), both shipped in PR #1840
// (#1201). The resulting tilesets are served by the existing scene routes
// (/scenes/{id}/tileset.json + /scenes/{id}/{*assetPath}). These records mirror
// the server wire contract so the console can discover renderable point-cloud
// scenes and view them in a 3D Tiles viewer.

/// <summary>Bounding box of a scene in WGS-84 degrees.</summary>
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

/// <summary>A discoverable renderable scene summary from <c>GET /api/scenes</c>.</summary>
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

/// <summary>The <c>GET /api/scenes</c> discovery list response.</summary>
public sealed record SceneListResponse
{
    [JsonPropertyName("scenes")]
    public IReadOnlyList<SceneSummary> Scenes { get; init; } = [];
}

/// <summary>
/// Result of <c>POST /api/v1/admin/scenes/ingest/pointcloud</c>: a servable
/// 3D Tiles point tileset plus ingest statistics.
/// </summary>
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

/// <summary>Read/operation-status vocabulary for the scene surface.</summary>
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

/// <summary>Read/ingest envelope whose status drives the shared scene surfaces.</summary>
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

/// <summary>A LAS point cloud to ingest (in-memory bytes + optional metadata).</summary>
public sealed record PointCloudIngestRequest
{
    public required byte[] Content { get; init; }

    public required string FileName { get; init; }

    public string? DisplayName { get; init; }

    public string? Description { get; init; }

    public int? SourceCrs { get; init; }
}

/// <summary>Consistent titles/copy for non-ok scene states.</summary>
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
