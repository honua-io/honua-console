using Honua.Console.Contracts;

namespace Honua.Console.Native.Core.Tests;

/// <summary>
/// Test-only no-op <see cref="IStudioMapGenerationClient"/> for the map data-source tests that exercise the
/// non-generation lifecycle (load/save/publish/reopen). Returns a 404 "Unsupported" so any incidental
/// GenerateAsync call surfaces the honest unavailable state rather than throwing. Generation behaviour itself
/// is covered by its own dedicated tests with a recording fake.
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
