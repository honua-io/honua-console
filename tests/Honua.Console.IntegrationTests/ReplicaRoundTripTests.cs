using System.Text;
using System.Text.Json;
using Honua.Console.Shell.Services;

namespace Honua.Console.IntegrationTests;

[Collection(TemporalReplicaIntegrationCollection.Name)]
public sealed class ReplicaRoundTripTests
{
    private readonly TemporalReplicaFixture _fixture;

    public ReplicaRoundTripTests(TemporalReplicaFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task NamedReplica_LandsServerSide_AndConsoleReplicaListReflectsIt()
    {
        Skip.If(_fixture.SkipReason is not null, _fixture.SkipReason ?? string.Empty);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var seeded = await PublishedLayerSeeder.SeedPublishedLayerAsync(_fixture, suffix);
        Skip.If(
            seeded is null,
            "The pinned honua-server image could not service the layer-publish path; the replica round-trip "
            + "needs a published service/layer to register a replica against.");

        var replicaName = $"console-replica-{suffix}";
        var created = await CreateReplicaAsync(seeded!.ServiceName, replicaName);
        Skip.If(
            created.Skip,
            "The pinned honua-server image has no GeoServices createReplica verb or offline-sync entitlement; "
            + "the replica round-trip needs a server build that serves that path.");
        Assert.True(created.Failure is null, created.Failure);
        var replicaId = created.ReplicaId;

        using var verifier = _fixture.CreateVerifier();

        // --- Independent server read FIRST: the replica registry lists the named replica we just created. ---
        var serverReplicas = await verifier.ListReplicasAsync(seeded.ServiceName);
        var serverReplica = Assert.Single(serverReplicas, r => r.ReplicaId == replicaId);
        Assert.Equal(replicaName, serverReplica.ReplicaName);
        Assert.Equal(seeded.ServiceName, serverReplica.ServiceId);

        // --- Independent server read: the single-replica detail resolves the same replica. ---
        var serverDetail = await verifier.GetReplicaAsync(seeded.ServiceName, replicaId!);
        Assert.NotNull(serverDetail);
        Assert.Equal(replicaId, serverDetail!.ReplicaId);
        Assert.Equal(replicaName, serverDetail.ReplicaName);

        // --- Negative (independent): a foreign replica id is not-found, not silently coerced to another. ---
        var foreign = await verifier.GetReplicaAsync(seeded.ServiceName, $"missing-{Guid.NewGuid():N}");
        Assert.Null(foreign);

        // --- OPERATION under test: the console replica list (the temporal/replica queue) renders the replica.
        // The console keys replicas by a configured source candidate ("serviceId/layerId"); configure the
        // seeded layer so the console resolves the source and reads the live replica registry.
        var consoleClient = new HonuaServerTemporalCapabilityClient(
            _fixture.CreateTemporalClient(),
            new HonuaServerTemporalOptions([new TemporalSourceCandidate(seeded.ServiceName, seeded.LayerId)]));

        var sourceKey = $"{seeded.ServiceName}/{seeded.LayerId}";
        var queue = await consoleClient.GetReplicaConflictQueueAsync(sourceKey);

        // The console reflects the server registry (no binding error) and surfaces the same named replica.
        Assert.Null(queue.BindingState);
        var consoleReplica = Assert.Single(queue.Replicas, r => r.ReplicaId == replicaId);
        Assert.Equal(replicaName, consoleReplica.ReplicaName);
        Assert.Equal(sourceKey, consoleReplica.SourceId);
        // Pending-conflict counts come from the deferred conflict-review slice (#1287) — never fabricated.
        Assert.Equal(0, consoleReplica.PendingConflictCount);
    }

    // Creates a named replica through the GeoServices FeatureServer createReplica verb (the durable-sync
    // entry point, mirroring honua-server's ReplicaManagementEndpointTests seed).
    private async Task<ReplicaCreateResponses.Decision> CreateReplicaAsync(string serviceName, string replicaName)
    {
        using var http = _fixture.CreateRawClient();
        if (!string.IsNullOrWhiteSpace(_fixture.AdminApiKey))
        {
            http.DefaultRequestHeaders.TryAddWithoutValidation("X-API-Key", _fixture.AdminApiKey);
        }

        var payload = JsonSerializer.Serialize(new
        {
            replicaName,
            layers = "0",
            syncModel = "perReplica",
            f = "json"
        });

        using var response = await http.PostAsync(
            $"/rest/services/{Uri.EscapeDataString(serviceName)}/FeatureServer/createReplica",
            new StringContent(payload, Encoding.UTF8, "application/json"));
        var content = await response.Content.ReadAsStringAsync();
        return ReplicaCreateResponses.Classify((int)response.StatusCode, content);
    }
}
