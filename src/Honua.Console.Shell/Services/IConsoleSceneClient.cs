using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public interface IConsoleSceneClient
{
    Task<SceneReadResult<SceneListResponse>> ListScenesAsync(
        CancellationToken cancellationToken = default);

    Task<SceneReadResult<PointCloudIngestResult>> IngestPointCloudAsync(
        PointCloudIngestRequest request,
        CancellationToken cancellationToken = default);

    Task<string?> ResolveTilesetUrlAsync(
        string sceneId,
        CancellationToken cancellationToken = default);
}
