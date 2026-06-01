using System.Net;
using System.Text;
using System.Text.Json;
using Honua.Console.Shell.Services;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Named-replica round-trip (console-integration-test-plan.md Wave 3, Family F, P1; honua-server#1167).
///
/// Seeds a real queryable service/layer, then creates a NAMED replica server-side through the GeoServices
/// FeatureServer <c>createReplica</c> verb (the durable-sync entry point operators see). It then drives the
/// production <see cref="HonuaServerTemporalCapabilityClient"/> replica list (the console replica OPERATION
/// path) and asserts the result INDEPENDENTLY through the <see cref="ServerStateVerifier"/> oracle hitting
/// the server's own replica-management API (<c>/api/v1/admin/services/{serviceId}/replicas</c> + detail) —
/// proving the console replica list reflects the server-owned replica registry (rule #2: a DIFFERENT read
/// API than the console client). The negative companion asserts a foreign replica id is not-found
/// independently.
///
/// Off by default; the SkippableFacts skip cleanly without Docker / the opt-in env (Console Patterns Charter
/// section 11) and RUN in the nightly lane.
/// </summary>
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
        var replicaId = await CreateReplicaAsync(seeded!.ServiceName, replicaName);
        Skip.If(
            replicaId is null,
            "The pinned honua-server image could not service the GeoServices createReplica path; the replica "
            + "round-trip needs a server build whose disconnected-sync createReplica verb is ready.");

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
    // entry point, mirroring honua-server's ReplicaManagementEndpointTests seed). Returns the server-issued
    // replica id, or null when the pinned image cannot service the path so the caller can skip cleanly.
    private async Task<string?> CreateReplicaAsync(string serviceName, string replicaName)
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

        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed
            or HttpStatusCode.NotImplemented
            || (int)response.StatusCode >= 500)
        {
            return null;
        }

        var content = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        using var document = JsonDocument.Parse(content);
        // The GeoServices createReplica response carries the replica id as "replicaID".
        if (document.RootElement.TryGetProperty("replicaID", out var id) && id.ValueKind == JsonValueKind.String)
        {
            return id.GetString();
        }

        return null;
    }
}
