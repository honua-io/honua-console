using Honua.Console.Contracts;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Test-only no-op <see cref="IStudioMapGenerationClient"/> for the map render/integration tests that exercise
/// the non-generation lifecycle. Returns a 404 "Unsupported" so any incidental GenerateAsync call surfaces the
/// honest unavailable state rather than throwing.
/// </summary>
internal sealed class NoopStudioMapGenerationClient : IStudioMapGenerationClient
{
    public Uri BaseUri { get; } = new("https://server.example");

    public Task<StudioEndpointResult<MapGenerationResult>> GenerateMapAsync(
        GenerateMapPackageRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(StudioEndpointResult<MapGenerationResult>.FromIssue(
            new StudioEndpointIssue("Unsupported", "POST generate", "not configured", 404)));
}
