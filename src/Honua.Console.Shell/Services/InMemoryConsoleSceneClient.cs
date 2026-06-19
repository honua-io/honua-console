using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Test/demo-only scene client backed by an in-memory scene catalog. Lets
/// render/integration tests exercise the discovery list, the LAS ingest flow
/// (upload -> result), and tileset URL resolution without a live server. NEVER
/// the DI default — opt-in for tests, like the other InMemory* clients.
/// </summary>
public sealed class InMemoryConsoleSceneClient : IConsoleSceneClient
{
    private readonly List<SceneSummary> _scenes;
    private readonly string _baseUrl;

    public InMemoryConsoleSceneClient(
        IEnumerable<SceneSummary>? scenes = null,
        string baseUrl = "https://example.honua.local")
    {
        _scenes = scenes?.ToList() ?? CreateSeed();
        _baseUrl = baseUrl.TrimEnd('/');
    }

    public Task<SceneReadResult<SceneListResponse>> ListScenesAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult(SceneReadResult<SceneListResponse>.Ok(
            new SceneListResponse { Scenes = _scenes.ToList() }));

    public Task<SceneReadResult<PointCloudIngestResult>> IngestPointCloudAsync(
        PointCloudIngestRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Content.Length == 0)
        {
            return Task.FromResult(SceneReadResult<PointCloudIngestResult>.Denied(
                SceneReadStatus.InvalidInput, "Select a non-empty LAS point cloud to ingest."));
        }

        var sceneId = $"pc-{Guid.NewGuid():N}"[..12];
        var result = new PointCloudIngestResult
        {
            SceneId = sceneId,
            TilesetUrl = $"{_baseUrl}/scenes/{sceneId}/tileset.json",
            PointCount = Math.Max(1, request.Content.Length / 34),
            TileCount = 4,
            HasColor = true,
            BoundingRegionDegrees = [-122.5, 37.7, -122.4, 37.8]
        };

        _scenes.Add(new SceneSummary
        {
            Id = sceneId,
            Name = request.DisplayName ?? request.FileName,
            Description = request.Description,
            TilesetUrl = result.TilesetUrl,
            Bounds = new SceneBounds { West = -122.5, South = 37.7, East = -122.4, North = 37.8 },
            Capabilities = ["3dtiles", "pointcloud"],
            UpdatedAt = DateTimeOffset.UtcNow
        });

        return Task.FromResult(SceneReadResult<PointCloudIngestResult>.Ok(result));
    }

    public Task<string?> ResolveTilesetUrlAsync(
        string sceneId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(string.IsNullOrWhiteSpace(sceneId)
            ? null
            : $"{_baseUrl}/scenes/{Uri.EscapeDataString(sceneId)}/tileset.json");

    private static List<SceneSummary> CreateSeed() =>
    [
        new SceneSummary
        {
            Id = "demo-pointcloud",
            Name = "Demo point cloud",
            Description = "Synthetic LAS-derived 3D Tiles point cloud.",
            TilesetUrl = "https://example.honua.local/scenes/demo-pointcloud/tileset.json",
            Bounds = new SceneBounds { West = -122.5, South = 37.7, East = -122.4, North = 37.8 },
            Capabilities = ["3dtiles", "pointcloud"],
            UpdatedAt = DateTimeOffset.UtcNow
        }
    ];
}
