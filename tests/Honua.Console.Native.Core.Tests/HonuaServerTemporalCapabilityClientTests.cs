using Honua.Console.Contracts;
using Honua.Console.Shell.Services;

namespace Honua.Console.Native.Core.Tests;

/// <summary>
/// Host-independent coverage for the server-bound temporal client
/// (<see cref="HonuaServerTemporalCapabilityClient"/>) against the deserialized-DTO null-safety contract:
/// System.Text.Json overrides a collection's <c>[]</c> initializer with null when the server emits an
/// explicit JSON <c>null</c> for the key, so the replica-list read must coalesce before LINQ rather than
/// throwing and tearing down the Blazor circuit (Console Patterns Charter section 11 — bind the real
/// server, never throw on an honest-but-empty payload).
/// </summary>
public sealed class HonuaServerTemporalCapabilityClientTests
{
    private const string ServiceId = "svc-parcels";
    private const string SourceId = "svc-parcels/0";

    private static HonuaServerTemporalCapabilityClient CreateClient(IHonuaTemporalClient client) =>
        new(client, new HonuaServerTemporalOptions([new TemporalSourceCandidate(ServiceId, 0)]));

    [Fact]
    public async Task GetReplicaConflictQueue_ServerOmitsReplicas_ReturnsEmptyBoundQueueWithoutThrowing()
    {
        // The server returns a success body whose "replicas" key is an explicit JSON null; STJ leaves the
        // non-null-typed Replicas array null. The read must not throw.
        var fake = new FakeTemporalClient(
            HonuaAdminEndpointResult<HonuaReplicaManagementListResponse>.FromData(
                new HonuaReplicaManagementListResponse { Replicas = null! }));

        var queue = await CreateClient(fake).GetReplicaConflictQueueAsync(SourceId);

        Assert.Empty(queue.Replicas);
        // A successful read is bound (no binding-state issue), distinguishing it from the missing/unsupported
        // paths — the empty result reflects a real server payload, not a fabricated or unbound state.
        Assert.Null(queue.BindingState);
        Assert.Equal(ServiceId, fake.LastListReplicasServiceId);
    }

    [Fact]
    public async Task GetReplicaConflictQueue_UnconfiguredSource_ReportsMissingBinding_WithoutCallingServer()
    {
        var fake = new FakeTemporalClient(
            HonuaAdminEndpointResult<HonuaReplicaManagementListResponse>.FromData(
                new HonuaReplicaManagementListResponse()));

        var queue = await CreateClient(fake).GetReplicaConflictQueueAsync("not-configured/0");

        Assert.Empty(queue.Replicas);
        Assert.NotNull(queue.BindingState);
        Assert.Null(fake.LastListReplicasServiceId);
    }

    private sealed class FakeTemporalClient(
        HonuaAdminEndpointResult<HonuaReplicaManagementListResponse> listResult) : IHonuaTemporalClient
    {
        public string? LastListReplicasServiceId { get; private set; }

        public Uri BaseUri { get; } = new("https://temporal.test/");

        public Task<HonuaAdminEndpointResult<HonuaReplicaManagementListResponse>> ListReplicasAsync(
            string serviceId,
            CancellationToken cancellationToken = default)
        {
            LastListReplicasServiceId = serviceId;
            return Task.FromResult(listResult);
        }

        public Task<HonuaAdminEndpointResult<HonuaTemporalCapabilityResponse>> GetCapabilityAsync(
            string serviceId, int layerId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<HonuaAdminEndpointResult<HonuaTemporalAsOfResponse>> ReadAsOfAsync(
            string serviceId, int layerId, long? generation, string? timestamp, int? limit,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<HonuaAdminEndpointResult<HonuaReplicaManagementDetail>> GetReplicaAsync(
            string serviceId, string replicaId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
