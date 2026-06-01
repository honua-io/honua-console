using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Proves the server-bound Operate &gt; Catalogs discovery-endpoints surface reaches a real honua-server
/// started via Testcontainers and round-trips the catalog discovery-endpoints registry read contract
/// (honua-server#1279). Drives the production <see cref="HonuaServerCatalogDiscoveryDataSource"/> over the
/// live <c>/api/v1/console/catalog-endpoints/...</c> routes with the admin API key.
///
/// The shipped server registers an empty config-backed registry store (no fabricated sample dialects), so a
/// stock <c>:nightly</c> deployment publishes no discovery endpoints for an arbitrary workspace: the
/// registry read returns the structured admin NotFound envelope, which the live shim maps to the explicit
/// "Unsupported" capability state. That is the round-trip proof against stock nightly — the route is mounted,
/// admin auth is accepted (not a 401 "Missing permission"), and the response deserializes through the shared
/// envelope (not a transport "Unavailable" failure). A deployment that seeds a workspace registry can set
/// <c>HONUA_CONSOLE_CATALOG_WORKSPACE</c> to additionally assert a populated registry round-trips. Docker-
/// unavailable environments skip cleanly.
/// </summary>
[Collection(CatalogsDiscoveryIntegrationCollection.Name)]
public sealed class CatalogsDiscoveryLiveServerTests
{
    private readonly CatalogsDiscoveryFixture _fixture;

    public CatalogsDiscoveryLiveServerTests(CatalogsDiscoveryFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task CatalogDiscovery_LiveBindingReachesRegistryAndRoundTripsContract()
    {
        Skip.If(_fixture.SkipReason is not null, _fixture.SkipReason ?? string.Empty);

        var dataSource = new HonuaServerCatalogDiscoveryDataSource(_fixture.CreateCatalogDiscoveryClient());

        var seededWorkspace = Environment.GetEnvironmentVariable("HONUA_CONSOLE_CATALOG_WORKSPACE");
        if (!string.IsNullOrWhiteSpace(seededWorkspace))
        {
            // A deployment that publishes a workspace registry: assert a populated round-trip end to end.
            var seeded = await dataSource.LoadRegistryAsync(seededWorkspace);
            Assert.Empty(seeded.CapabilityStates);
            Assert.NotNull(seeded.Registry);
            Assert.Equal(seededWorkspace, seeded.Registry!.WorkspaceId);
            return;
        }

        // Stock nightly: no workspace publishes a registry, so the registry read round-trips the structured
        // NotFound envelope into the "Unsupported" capability state. The live binding genuinely reached the
        // mounted route over the wire — it was NOT a transport failure ("Unavailable") nor an auth rejection
        // ("Missing permission"), which proves the admin-authorized request and the contract round-trip.
        var load = await dataSource.LoadRegistryAsync($"console-catalog-probe-{Guid.NewGuid():N}");

        Assert.Null(load.Registry);
        var state = Assert.Single(load.CapabilityStates);
        Assert.Equal("Catalogs (discovery endpoints)", state.Surface);
        Assert.Equal("Unsupported", state.State);
        Assert.NotEqual("Unavailable", state.State);
        Assert.NotEqual("Missing permission", state.State);
        Assert.NotEqual("Missing binding", state.State);
        Assert.Contains("/api/v1/console/catalog-endpoints", state.Contract, StringComparison.Ordinal);
    }
}
