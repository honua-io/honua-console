using System.Net;
using Honua.Console.Contracts;
using Honua.Console.Shell.Services;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Publishes real layers through the supported API, then verifies the mapped workspace registry,
/// Console detail/item reflection, namespace isolation and authentication against the hosted server.
/// </summary>
[Collection(TemporalReplicaIntegrationCollection.Name)]
public sealed class CatalogDiscoveryRoundTripTests
{
    private const string Workspace = TemporalReplicaFixture.CatalogWorkspace;
    private readonly TemporalReplicaFixture _fixture;

    public CatalogDiscoveryRoundTripTests(TemporalReplicaFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task CatalogDiscovery_RouteMountedDiscriminator_And_ConsoleReflectsServerRegistry()
    {
        Skip.If(_fixture.SkipReason is not null, _fixture.SkipReason ?? string.Empty);
        using var verifier = _fixture.CreateVerifier();
        var anonymousStatus = await verifier.ProbeAdminRouteAnonymousStatusAsync(
            $"/api/v1/console/catalog-endpoints/{Workspace}");
        Assert.True(anonymousStatus is 401 or 403,
            $"The mounted catalog route must deny anonymous callers; received {anonymousStatus}.");

        // Mapping alone does not populate a registry. No synthetic catalog or metadata graph is seeded.
        var before = await verifier.GetCatalogDiscoveryRegistryAsync(Workspace);
        Assert.NotNull(before);
        Assert.Empty(before.Endpoints);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var published = await PublishedLayerSeeder.PublishNamespacedLayerAsync(
            _fixture, $"catalog_{suffix}", TemporalReplicaFixture.CatalogNamespace);
        var foreign = await PublishedLayerSeeder.PublishNamespacedLayerAsync(
            _fixture, $"foreign_{suffix}", "console-other-namespace");

        // An independent protocol read proves the configured registry describes a real served layer.
        var features = await verifier.QueryFeatureServerAsync(published.ServiceName, published.LayerId, "1=1");
        Assert.NotNull(features);
        Assert.Equal(3, features.Count);
        var serverRegistry = await verifier.GetCatalogDiscoveryRegistryAsync(Workspace);
        Assert.NotNull(serverRegistry);
        var serverEndpoint = Assert.Single(serverRegistry.Endpoints);
        Assert.Equal("esri", serverEndpoint.Key);
        Assert.True((serverEndpoint.Entries ?? 0) > 0);

        using var client = new HonuaCatalogDiscoveryHttpClient(_fixture.CreateRawClient(),
            new HonuaCatalogDiscoveryClientOptions(_fixture.BaseAddress, _fixture.AdminApiKey));
        var dataSource = new HonuaServerCatalogDiscoveryDataSource(client);
        var load = await dataSource.LoadRegistryAsync(Workspace);
        Assert.True(load.HasRegistry, DescribeStates(load.CapabilityStates));
        Assert.Empty(load.CapabilityStates);
        Assert.Equal(serverRegistry.WorkspaceId, load.Registry!.WorkspaceId);
        Assert.Equal(serverRegistry.Endpoints.Select(endpoint => endpoint.Key),
            load.Registry.Endpoints.Select(endpoint => endpoint.Key));

        var detail = await dataSource.LoadEndpointAsync(Workspace, "esri");
        Assert.True(detail.HasDetail, DescribeStates(detail.CapabilityStates));
        Assert.Equal(serverEndpoint.Entries, detail.Detail!.Items.Count);
        Assert.NotEmpty(detail.Detail.Items);
        Assert.All(detail.Detail.Items, item => Assert.Equal(published.ServiceName, item.Title));
        Assert.DoesNotContain(detail.Detail.Items, item => item.Title == foreign.ServiceName);
        foreach (var row in detail.Detail.Items)
        {
            var item = await dataSource.LoadItemAsync(Workspace, "esri", row.Id);
            Assert.True(item.HasItem, DescribeStates(item.CapabilityStates));
            Assert.Equal(row.Id, item.Item!.Id);
            Assert.Equal(published.ServiceName, item.Item.Title);
        }

        using var http = _fixture.CreateRawClient();
        http.DefaultRequestHeaders.Add("X-API-Key", _fixture.AdminApiKey);
        using var unknown = await http.GetAsync("/api/v1/console/catalog-endpoints/unmapped-workspace");
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        // The same API key resolves the configured public tenant, not a tenant selected by the workspace URL.
        using var wrongTenant = await http.GetAsync("/api/v1/console/catalog-endpoints/other-tenant-workspace");
        Assert.Equal(HttpStatusCode.NotFound, wrongTenant.StatusCode);
    }

    private static string DescribeStates(IReadOnlyList<Honua.Console.Shell.Models.CatalogDiscoveryCapabilityState> states) =>
        states.Count == 0
            ? "<no capability states>"
            : string.Join("; ", states.Select(state => $"{state.State}: {state.Detail}"));
}
