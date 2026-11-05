using System.Globalization;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.IntegrationTests;

// LayerPublishRequest cannot set the temporal column, so unsupported history returns HTTP 409 and the positive branch runs only when the image can.
[Collection(TemporalReplicaIntegrationCollection.Name)]
public sealed class TemporalCapabilityRoundTripTests
{
    private readonly TemporalReplicaFixture _fixture;

    public TemporalCapabilityRoundTripTests(TemporalReplicaFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task TemporalCapability_ForPublishedLayer_ConsoleReflectionMatchesServerCapability()
    {
        Skip.If(_fixture.SkipReason is not null, _fixture.SkipReason ?? string.Empty);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var seeded = await PublishedLayerSeeder.SeedPublishedLayerAsync(_fixture, suffix);
        Skip.If(
            seeded is null,
            "The pinned honua-server image could not service the layer-publish path; the temporal capability "
            + "round-trip needs a published service/layer to inspect.");

        var sourceKey = $"{seeded!.ServiceName}/{seeded.LayerId.ToString(CultureInfo.InvariantCulture)}";

        // --- Independent server read FIRST: the canonical temporal capability descriptor. ---
        using var verifier = _fixture.CreateVerifier();
        var serverCapability = await verifier.GetTemporalCapabilityAsync(seeded.ServiceName, seeded.LayerId);
        Skip.If(
            serverCapability is null,
            "The pinned honua-server image does not expose the temporal capability route (honua-server#1166); "
            + "the temporal round-trip needs a server build whose /api/v1/temporal path is mounted.");

        // --- OPERATION under test: the console temporal client reads the same source's capability. ---
        var temporalClient = _fixture.CreateTemporalClient();
        var consoleClient = new HonuaServerTemporalCapabilityClient(
            temporalClient,
            new HonuaServerTemporalOptions([new TemporalSourceCandidate(seeded.ServiceName, seeded.LayerId)]));

        var workspace = await consoleClient.GetWorkspaceAsync();

        // The console surfaced exactly the configured source as a live capability (not a fabricated one, and
        // not a capability-state error) — the console temporal binding reached the server for this layer.
        var consoleSource = Assert.Single(workspace.Sources);
        Assert.Equal(sourceKey, consoleSource.SourceId);
        Assert.Equal(seeded.LayerId.ToString(CultureInfo.InvariantCulture), consoleSource.LayerId);

        // The console maps supportsHistory/supportsAsOf into a temporal mode; assert that reflection agrees
        // with the SERVER-owned capability for this layer (rule #2). A layer published through the live admin
        // path has no temporal column, so the server reports unsupported and the console mode is None.
        if (serverCapability!.SupportsHistory)
        {
            Assert.NotEqual(TemporalMode.None, consoleSource.Mode);

            // Positive as-of: the console checkpoint list surfaces the current generation cursor, and the
            // server as-of read returns the collapsed change set independently.
            var checkpoints = await consoleClient.GetCheckpointsAsync(sourceKey);
            Assert.Null(checkpoints.BindingState);
            var checkpoint = Assert.Single(checkpoints.Checkpoints);

            var serverAsOf = await verifier.ReadTemporalAsOfAsync(seeded.ServiceName, seeded.LayerId, generation: 0);
            Assert.NotNull(serverAsOf);
            // The console's checkpoint cursor is the server's current generation — same value, two paths.
            Assert.Equal(
                serverCapability.CurrentGeneration?.ToString(CultureInfo.InvariantCulture),
                checkpoint.CursorValue);
            Assert.Equal("Generation", serverAsOf!.RequestedCursorKind);
        }
        else
        {
            // Negative round-trip (the plan's explicit negative): the layer is non-temporal. The console maps
            // this to mode None, and the console checkpoint read surfaces the unsupported/not-yet-available
            // binding state rather than fabricating a checkpoint.
            Assert.Equal(TemporalMode.None, consoleSource.Mode);

            // The server as-of read independently confirms the layer is non-temporal (409 → null here).
            var serverAsOf = await verifier.ReadTemporalAsOfAsync(seeded.ServiceName, seeded.LayerId, generation: 0);
            Assert.Null(serverAsOf);

            var checkpoints = await consoleClient.GetCheckpointsAsync(sourceKey);
            Assert.Empty(checkpoints.Checkpoints);
            Assert.NotNull(checkpoints.BindingState);
        }
    }

    [SkippableFact]
    public async Task TemporalCapability_ForUnconfiguredSource_ConsoleReportsCapabilityState_NeverFabricates()
    {
        Skip.If(_fixture.SkipReason is not null, _fixture.SkipReason ?? string.Empty);

        // No candidate sources configured: the console must surface a capability explanation rather than an
        // empty/fabricated viewer (the temporal API has no enumeration verb). This needs no server contract,
        // but it is the standing no-fabrication guarantee for the live temporal binding.
        var temporalClient = _fixture.CreateTemporalClient();
        var consoleClient = new HonuaServerTemporalCapabilityClient(
            temporalClient,
            new HonuaServerTemporalOptions([]));

        var workspace = await consoleClient.GetWorkspaceAsync();

        Assert.Empty(workspace.Sources);
        var state = Assert.Single(workspace.CapabilityStates);
        Assert.Equal("Not configured", state.State);
    }
}
