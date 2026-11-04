using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public sealed class UnsupportedConsoleSceneClient : IConsoleSceneClient
{
    public Task<SceneReadResult<SceneListResponse>> ListScenesAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult(SceneReadResult<SceneListResponse>.Denied(
            SceneReadStatus.Unavailable, ScenePresentation.MissingBindingMessage));

    public Task<SceneReadResult<PointCloudIngestResult>> IngestPointCloudAsync(
        PointCloudIngestRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(SceneReadResult<PointCloudIngestResult>.Denied(
            SceneReadStatus.Unavailable, ScenePresentation.MissingBindingMessage));

    public Task<string?> ResolveTilesetUrlAsync(
        string sceneId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(null);
}
