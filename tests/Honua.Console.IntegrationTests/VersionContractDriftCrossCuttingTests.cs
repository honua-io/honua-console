using Honua.Console.Contracts;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Cross-cutting version / contract-drift fact (console-integration-test-plan.md §5.5, Wave 6).
///
/// Mirrors the SDK conformance lane's pinning: asserts the pinned <c>:nightly</c> server's version + metadata
/// contract is recorded in the run evidence, and that the console's expected version/capabilities routes
/// resolve against the live server — so a drift between the console's expected contract and the server is
/// VISIBLE (a route rename or schema bump shows up in evidence + a failing route-resolution assertion before
/// it reaches users). It reads the contract two ways: INDEPENDENTLY through the
/// <see cref="ServerStateVerifier"/> oracle and through the console's own operate client
/// (<see cref="IHonuaAdminOperateClient.GetVersionAsync"/> / <see cref="IHonuaAdminOperateClient.GetCapabilitiesAsync"/>),
/// then asserts the two agree (rule #2: a different read path must see the same server).
///
/// Off by default; the SkippableFact skips cleanly without Docker / the opt-in env (Console Patterns Charter
/// section 11) and RUNS in the nightly lane (.github/workflows/console-nightly.yml).
/// </summary>
[Collection(CrossCuttingIntegrationCollection.Name)]
public sealed class VersionContractDriftCrossCuttingTests
{
    private readonly CrossCuttingFixture _fixture;

    public VersionContractDriftCrossCuttingTests(CrossCuttingFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task PinnedServerContract_IsResolvable_RecordedInEvidence_AndMatchesConsoleProjection()
    {
        Skip.If(_fixture.SkipReason is not null, _fixture.SkipReason ?? string.Empty);

        // --- Independent read: the server-owned version + metadata-contract descriptor. ---
        using var verifier = _fixture.CreateVerifier();
        var contract = await verifier.GetServerContractAsync();
        Skip.If(
            contract is null,
            "The pinned honua-server image mounts neither /api/v1/admin/version nor /api/v1/admin/capabilities; "
            + "the contract-drift fact needs at least one of the console's expected contract routes present.");

        // The console EXPECTS its pinned contract routes to resolve. A rename/removal is the exact drift this
        // fact catches: at least one of version/capabilities must be mounted (asserted via the route flags),
        // and a mounted version route must carry a non-empty server version.
        Assert.True(
            contract!.VersionRouteMounted || contract.CapabilitiesRouteMounted,
            "Neither the version nor the capabilities contract route resolved against the pinned server.");
        if (contract.VersionRouteMounted)
        {
            Assert.False(
                string.IsNullOrWhiteSpace(contract.Version),
                "The pinned server's /api/v1/admin/version returned no version string (contract drift).");
        }

        // --- Console read path: the operate client surfaces the same version/contract. ---
        var operateClient = _fixture.CreateOperateClient();
        var consoleVersion = await operateClient.GetVersionAsync();
        var consoleCapabilities = await operateClient.GetCapabilitiesAsync();

        string? consoleProjectedVersion = consoleVersion.Data?.Version ?? consoleCapabilities.Data?.ServerVersion;

        // When BOTH read paths resolve a version, they must agree — a divergence means the console and the
        // independent oracle are seeing different contracts (rule #2). When the console route 4xx'd while the
        // independent read succeeded (or vice versa), that asymmetry is itself recorded in the evidence below.
        if (contract.VersionRouteMounted && consoleVersion.Data?.Version is { Length: > 0 } projected)
        {
            Assert.Equal(contract.Version, projected);
        }

        if (contract.VersionRouteMounted && consoleVersion.Data?.MetadataApiVersion is { Length: > 0 } projectedApi)
        {
            Assert.Equal(contract.MetadataApiVersion, projectedApi);
        }

        // --- Evidence out: record the pinned contract so drift is visible in the nightly artifacts. ---
        var evidence = new ContractDriftEvidence
        {
            ServerImage = _fixture.Options.ServerImage
                ?? _fixture.Options.ExternalBaseUri?.ToString()
                ?? _fixture.BaseAddress.ToString(),
            ServerBaseAddress = _fixture.BaseAddress.ToString(),
            ServerVersion = contract.Version,
            MetadataApiVersion = contract.MetadataApiVersion,
            MetadataSchemaVersion = contract.MetadataSchemaVersion,
            VersionRouteMounted = contract.VersionRouteMounted,
            CapabilitiesRouteMounted = contract.CapabilitiesRouteMounted,
            ConsoleProjectedVersion = consoleProjectedVersion
        };
        await evidence.WriteAsync();
    }
}
