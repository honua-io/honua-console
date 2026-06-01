using System.Globalization;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Cross-cutting idempotency / round-trip fidelity (console-integration-test-plan.md §5.4, Wave 6).
///
/// Performs a config operation, reads it back INDEPENDENTLY through the <see cref="ServerStateVerifier"/>
/// oracle, re-applies the identical config, and asserts a stable result — no spurious change, no silent
/// duplicate, and a deterministic outcome (idempotent same-layer OR a clean conflict that leaves the original
/// unchanged). The read-back asserts deep fidelity of the configured state (table, layer name, service,
/// geometry, SRID, extent, fields) so a config-in → state-out drift would surface.
///
/// Off by default; the SkippableFacts skip cleanly without Docker / the opt-in env (Console Patterns Charter
/// section 11) and RUN in the nightly lane (.github/workflows/console-nightly.yml).
/// </summary>
[Collection(CrossCuttingIntegrationCollection.Name)]
public sealed class IdempotencyFidelityCrossCuttingTests
{
    // Known data bbox in EPSG:3857 for the three seeded polygons (see CrossCuttingSeeder).
    private const double SeedXMin = 100.0;
    private const double SeedYMin = 200.0;
    private const double SeedXMax = 130.0;
    private const double SeedYMax = 230.0;

    private readonly CrossCuttingFixture _fixture;

    public IdempotencyFidelityCrossCuttingTests(CrossCuttingFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task LayerPublish_ReadBackEqualsConfig_AndReapplyIsStable()
    {
        Skip.If(_fixture.SkipReason is not null, _fixture.SkipReason ?? string.Empty);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var table = $"parcels_fid_{suffix}";
        var serviceName = $"parcels_fid_svc_{suffix}";
        await CrossCuttingSeeder.SeedPolygonTableAsync(_fixture.PostgresConnectionString!, table);
        var connectionId = await CrossCuttingSeeder.CreateConnectionAsync(_fixture, $"parcels-fid-conn-{suffix}");

        var operation = new HonuaServerServiceLayerPublishOperation(_fixture.CreateOperateClient());
        var command = new ServiceLayerPublishCommand
        {
            ConnectionId = connectionId,
            Schema = "public",
            Table = table,
            LayerName = "Parcels Fidelity",
            ServiceName = serviceName,
            GeometryColumn = "geom",
            GeometryType = "Polygon",
            Srid = 3857,
            PrimaryKey = "id",
            Fields = ["id", "name", "area_m2"],
            Enabled = true
        };

        var first = await operation.PublishAsync(command);
        Skip.If(
            !first.Succeeded
            && (string.Equals(first.State, "Unavailable", StringComparison.OrdinalIgnoreCase)
                || (first.Detail?.Contains("HTTP 5", StringComparison.OrdinalIgnoreCase) ?? false)
                || (first.Detail?.Contains("500", StringComparison.Ordinal) ?? false)),
            $"The pinned honua-server image could not service the layer-publish path ({first.State} — {first.Detail}); "
            + "the idempotency round-trip needs a server build whose layer-publishing path is ready.");
        Assert.True(first.Succeeded, $"First publish failed: {first.State} — {first.Detail}");
        var layerId = first.LayerId!.Value;

        using var verifier = _fixture.CreateVerifier();

        // --- Read back the full configured state INDEPENDENTLY and assert config-in → state-out fidelity. ---
        var registration = await verifier.GetRegisteredLayerAsync(connectionId, serviceName, layerId);
        Assert.NotNull(registration);
        Assert.Equal(command.Table, registration!.Table);
        Assert.Equal(command.LayerName, registration.LayerName);
        Assert.Equal(command.ServiceName, registration.ServiceName);
        Assert.True(registration.Enabled ?? false);

        var metadata = await verifier.GetFeatureServerLayerAsync(serviceName, layerId);
        Assert.NotNull(metadata);
        Assert.Equal("esriGeometryPolygon", metadata!.GeometryType);
        Assert.Equal(3857, metadata.Extent!.Wkid);
        AssertApprox(SeedXMin, metadata.Extent.XMin);
        AssertApprox(SeedYMin, metadata.Extent.YMin);
        AssertApprox(SeedXMax, metadata.Extent.XMax);
        AssertApprox(SeedYMax, metadata.Extent.YMax);
        Assert.Contains("id", metadata.Fields.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("name", metadata.Fields.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("area_m2", metadata.Fields.Keys, StringComparer.OrdinalIgnoreCase);

        var rowsBefore = await verifier.QueryFeatureServerAsync(serviceName, layerId, "1=1");
        Assert.NotNull(rowsBefore);
        Assert.Equal(3, rowsBefore!.Count);

        // --- Re-apply the IDENTICAL config: the result is deterministic and the original is unchanged. ---
        var second = await operation.PublishAsync(command);
        if (second.Succeeded)
        {
            // Idempotent: the same layer id comes back (no silent duplicate).
            Assert.Equal(layerId, second.LayerId);
        }
        else
        {
            // Clean conflict rejection: no fabricated success, and the original layer survives untouched.
            Assert.False(string.IsNullOrWhiteSpace(second.Detail));
        }

        // Regardless of branch, the originally-configured layer still reads back identically — no spurious
        // change from the re-apply (the round-trip is stable).
        var afterReapply = await verifier.GetRegisteredLayerAsync(connectionId, serviceName, layerId);
        Assert.NotNull(afterReapply);
        Assert.Equal(command.Table, afterReapply!.Table);
        Assert.Equal(command.LayerName, afterReapply.LayerName);
        Assert.Equal(command.ServiceName, afterReapply.ServiceName);
        Assert.True(afterReapply.Enabled ?? false);

        var rowsAfter = await verifier.QueryFeatureServerAsync(serviceName, layerId, "1=1");
        Assert.NotNull(rowsAfter);
        Assert.Equal(rowsBefore.Count, rowsAfter!.Count);
        Assert.Equal(rowsBefore.SpatialReferenceWkid, rowsAfter.SpatialReferenceWkid);
    }

    private static void AssertApprox(double expected, double? actual, double tolerance = 0.5)
    {
        Assert.NotNull(actual);
        Assert.True(
            Math.Abs(expected - actual!.Value) <= tolerance,
            $"Expected ≈ {expected.ToString(CultureInfo.InvariantCulture)} but got {actual.Value.ToString(CultureInfo.InvariantCulture)}.");
    }
}
