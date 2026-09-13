using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.Native.Core.Tests;

/// <summary>
/// The map builder's fallback when the live server cannot generate a map.
///
/// "Cannot generate" reaches the console three different ways, and every one of them used to end
/// somewhere different: a 404/501 on the route returned a bare "unsupported" notice with no map at all;
/// a body-level status="unsupported" seeded a catalog-bound baseline; and a 2xx whose body carries no
/// generation status (what 2026.1-rc.2's nightly-aot image does — it answers /studio/map-packages/generate
/// with a draft-package create, {packageId, package, warnings}) was read as an outright ERROR and told the
/// operator "I couldn't turn that into a map."
///
/// They all mean the same thing to an operator, so they all have to end the same way: a single-layer
/// baseline bound to a REAL published catalog layer, plainly labelled a baseline — or, when the catalog
/// offers nothing to bind, an honest "unsupported" and no map. Never a package that resolves against nothing.
/// </summary>
public sealed class StudioMapGenerationBaselineTests
{
    [Fact]
    public async Task Generate_RouteMissingButCatalogHasALayer_SeedsBaselineBoundToTheRealLayer()
    {
        var source = CreateSource(
            StudioEndpointResult<MapGenerationResult>.FromIssue(
                new StudioEndpointIssue("Unsupported", "POST generate", "Not found.", 404)),
            CatalogWith(("parcels", 4, "Parcels")));

        var outcome = await source.GenerateAsync(
            new StudioMapEditorState(),
            new StudioMapGenerationRequest { Prompt = "a map of parcels" });

        Assert.Equal(StudioMapGenerationStatuses.Generated, outcome.Status);
        var layer = Assert.Single(outcome.State!.Layers);
        Assert.Equal("4", layer.BoundLayerId);
        Assert.Equal("parcels", layer.BoundServiceId);
        Assert.Contains(outcome.Warnings, w => w.Contains("Baseline", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Generate_RouteMissingDuringRefinement_PreservesExistingMapWithoutAddingBaseline()
    {
        var source = CreateSource(
            StudioEndpointResult<MapGenerationResult>.FromIssue(
                new StudioEndpointIssue("Unsupported", "POST generate", "Not found.", 404)),
            CatalogWith(("parcels", 4, "Parcels")));
        var current = new StudioMapEditorState { Title = "Authored map" };
        current.Layers.Add(new StudioMapLayerEditor
        {
            BoundLayerId = "9",
            BoundServiceId = "authored",
            Title = "Authored layer",
            SourceRef = "service:authored/9",
            Visible = true,
        });

        var outcome = await source.GenerateAsync(
            current,
            new StudioMapGenerationRequest { Prompt = "refine the map" });

        Assert.Equal(StudioMapGenerationStatuses.Unsupported, outcome.Status);
        Assert.Null(outcome.State);
        var layer = Assert.Single(current.Layers);
        Assert.Equal("9", layer.BoundLayerId);
        Assert.Equal("authored", layer.BoundServiceId);
    }

    [Fact]
    public async Task Generate_RouteMissingAndCatalogEmpty_StaysUnsupportedRatherThanInventingALayer()
    {
        var source = CreateSource(
            StudioEndpointResult<MapGenerationResult>.FromIssue(
                new StudioEndpointIssue("Unsupported", "POST generate", "Not found.", 404)),
            CatalogWith());

        var outcome = await source.GenerateAsync(
            new StudioMapEditorState(),
            new StudioMapGenerationRequest { Prompt = "a map of parcels" });

        Assert.Equal(StudioMapGenerationStatuses.Unsupported, outcome.Status);
        Assert.Null(outcome.State);
    }

    [Fact]
    public async Task Generate_ResponseCarriesNoStatus_IsUnsupportedNotAnErrorAndSeedsTheBaseline()
    {
        // A 2xx that does not implement the generation contract means the same thing as a 404 on the route:
        // this server does not generate maps. Reading it as "error" dead-ended the operator.
        var source = CreateSource(
            StudioEndpointResult<MapGenerationResult>.FromData(new MapGenerationResult()),
            CatalogWith(("parcels", 4, "Parcels")));

        var outcome = await source.GenerateAsync(
            new StudioMapEditorState(),
            new StudioMapGenerationRequest { Prompt = "a map of parcels" });

        Assert.Equal(StudioMapGenerationStatuses.Generated, outcome.Status);
        Assert.Equal("4", Assert.Single(outcome.State!.Layers).BoundLayerId);
    }

    [Fact]
    public async Task Generate_ResponseCarriesNoStatusAndCatalogEmpty_SaysGenerationIsUnavailable()
    {
        var source = CreateSource(
            StudioEndpointResult<MapGenerationResult>.FromData(new MapGenerationResult()),
            CatalogWith());

        var outcome = await source.GenerateAsync(
            new StudioMapEditorState(),
            new StudioMapGenerationRequest { Prompt = "a map of parcels" });

        Assert.Equal(StudioMapGenerationStatuses.Unsupported, outcome.Status);
        Assert.Null(outcome.State);
        // The turn must say something; a status-less response carries no rationale of its own.
        Assert.False(string.IsNullOrWhiteSpace(outcome.Rationale));
    }

    [Fact]
    public async Task Generate_UnrecognisedStatus_StillSurfacesAsErrorAndSeedsNothing()
    {
        // The status-less allowance is narrow on purpose: a server that answers with a status the console
        // does not know is NOT "no AI here", and must not be laundered into a baseline that looks like a
        // successful generation.
        var source = CreateSource(
            StudioEndpointResult<MapGenerationResult>.FromData(new MapGenerationResult { Status = "exploded" }),
            CatalogWith(("parcels", 4, "Parcels")));

        var outcome = await source.GenerateAsync(
            new StudioMapEditorState(),
            new StudioMapGenerationRequest { Prompt = "a map of parcels" });

        Assert.Equal(StudioMapGenerationStatuses.Error, outcome.Status);
        Assert.Null(outcome.State);
    }

    [Fact]
    public async Task Generate_TransportFailure_StillBlocksTheSurfaceInsteadOfBaselining()
    {
        // A 503 is a broken server, not an absent capability: it must reach the operator as a blocked
        // surface, never as a quietly-substituted baseline map.
        var source = CreateSource(
            StudioEndpointResult<MapGenerationResult>.FromIssue(
                new StudioEndpointIssue("Unavailable", "POST generate", "Server unreachable.", 503)),
            CatalogWith(("parcels", 4, "Parcels")));

        var outcome = await source.GenerateAsync(
            new StudioMapEditorState(),
            new StudioMapGenerationRequest { Prompt = "a map of parcels" });

        Assert.NotEqual(StudioMapGenerationStatuses.Generated, outcome.Status);
        Assert.NotNull(outcome.BindingState);
        Assert.Null(outcome.State);
    }

    private static HonuaServerStudioMapPackageDataSource CreateSource(
        StudioEndpointResult<MapGenerationResult> generationResult,
        IOperateTransitionDataSource catalog)
    {
        // Generation never touches the package-lifecycle client; the handler throws so a regression that
        // started calling it fails loudly rather than silently hitting the network.
        var httpClient = new HttpClient(new UnreachableHandler()) { BaseAddress = ServerUri };
        return new HonuaServerStudioMapPackageDataSource(
            new HttpStudioPackageLifecycleClient(httpClient, new StudioPackageLifecycleClientOptions(ServerUri, "test-api-key")),
            new StubMapGenerationClient(generationResult),
            catalog);
    }

    private static readonly Uri ServerUri = new("https://server.example");

    private sealed class UnreachableHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException($"Generation must not call the lifecycle API ({request.Method} {request.RequestUri}).");
    }

    private static IOperateTransitionDataSource CatalogWith(params (string Service, int LayerId, string LayerName)[] layers) =>
        new StubCatalogDataSource(layers
            .GroupBy(l => l.Service, StringComparer.Ordinal)
            .Select(group => new OperateServiceDetail(
                group.Key,
                group.Key,
                "FeatureServer",
                "Running",
                "Server",
                group
                    .Select(l => new OperateServiceLayerProjection(l.LayerId, l.LayerName, "Polygon", $"res-{l.LayerId}", l.LayerName))
                    .ToArray(),
                [],
                []))
            .ToArray());

    private sealed class StubMapGenerationClient(StudioEndpointResult<MapGenerationResult> result) : IStudioMapGenerationClient
    {
        public Uri BaseUri { get; } = new("https://server.example");

        public Task<StudioEndpointResult<MapGenerationResult>> GenerateMapAsync(
            GenerateMapPackageRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }

    private sealed class StubCatalogDataSource(IReadOnlyList<OperateServiceDetail> services) : IOperateTransitionDataSource
    {
        public Task<OperateTransitionWorkspace> GetWorkspaceAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The map data source reads the layers view, not the whole workspace.");

        public Task<OperateServicesView> GetLayersViewAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new OperateServicesView(services, []));

        public Task<OperateConnectionSummary?> FindConnectionAsync(string connectionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<OperateConnectionSummary?>(null);

        public Task<OperateResourceEditPreview?> FindResourceEditAsync(string resourceId, CancellationToken cancellationToken = default) =>
            Task.FromResult<OperateResourceEditPreview?>(null);

        public Task<OperateServiceDetail?> FindServiceAsync(string serviceName, CancellationToken cancellationToken = default) =>
            Task.FromResult<OperateServiceDetail?>(services.FirstOrDefault(s => s.Name == serviceName));
    }
}
