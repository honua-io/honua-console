using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Drives the console's 3D scene surface against a connected honua-server: it
/// discovers renderable scenes (<c>GET /api/scenes</c>), ingests a LAS point
/// cloud into a servable 3D Tiles tileset
/// (<c>POST /api/v1/admin/scenes/ingest/pointcloud</c>, honua-server #1840 /
/// #1201), and resolves the absolute tileset.json URL a 3D Tiles viewer loads
/// (<c>/scenes/{id}/tileset.json</c>). Every operation returns a
/// <see cref="SceneReadResult{T}"/> whose status drives the shared
/// missing/forbidden/unsupported/enterprise surfaces.
/// </summary>
public interface IConsoleSceneClient
{
    /// <summary>Lists the renderable scenes published on the connected server.</summary>
    Task<SceneReadResult<SceneListResponse>> ListScenesAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Uploads a LAS point cloud as multipart/form-data and returns the resulting
    /// tileset identity + ingest statistics.
    /// </summary>
    Task<SceneReadResult<PointCloudIngestResult>> IngestPointCloudAsync(
        PointCloudIngestRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves the absolute <c>/scenes/{id}/tileset.json</c> URL for the active
    /// environment, used as the 3D Tiles viewer source. Returns null when no
    /// environment is connected.
    /// </summary>
    Task<string?> ResolveTilesetUrlAsync(
        string sceneId,
        CancellationToken cancellationToken = default);
}
