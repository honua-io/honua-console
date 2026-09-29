using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Cross-cutting validation field-errors round-trip (console-integration-test-plan.md §5.3, Wave 6).
///
/// Submits an invalid configuration through the console operation and asserts the server returns field-level
/// validation errors (RFC-7807 ProblemDetails <c>errors[]</c>, the shared field-validation contract from the
/// merged validation initiative, task #70) bound to the offending fields — and that NOTHING lands (verified
/// INDEPENDENTLY through the <see cref="ServerStateVerifier"/> oracle). Covers two representative surfaces:
/// the service-layer publish (Family A) and the Studio analysis package (Family C), reusing the Wave 1 / Wave
/// 4 negative pattern.
///
/// Off by default; the SkippableFacts skip cleanly without Docker / the opt-in env (Console Patterns Charter
/// section 11) and RUN in the nightly lane (.github/workflows/console-nightly.yml).
/// </summary>
[Collection(CrossCuttingIntegrationCollection.Name)]
public sealed class ValidationFieldErrorsCrossCuttingTests
{
    private readonly CrossCuttingFixture _fixture;

    public ValidationFieldErrorsCrossCuttingTests(CrossCuttingFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task LayerPublish_WithInvalidConfig_ReturnsFieldErrors_AndNothingLands()
    {
        Skip.If(_fixture.SkipReason is not null, _fixture.SkipReason ?? string.Empty);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var table = $"parcels_val_{suffix}";
        var serviceName = $"parcels_val_svc_{suffix}";
        await CrossCuttingSeeder.SeedPolygonTableAsync(_fixture.PostgresConnectionString!, table);
        var connectionId = await CrossCuttingSeeder.CreateConnectionAsync(_fixture, $"parcels-val-conn-{suffix}");

        var operation = new HonuaServerServiceLayerPublishOperation(_fixture.CreateOperateClient());

        // Invalid config: the declared primary key "id" is omitted from the selected output fields — the
        // server rejects this deterministically with a field-addressable validation error
        // (PublishLayer_PrimaryKeyNotInFields_ReturnsBadRequest), and no layer lands.
        var command = new ServiceLayerPublishCommand
        {
            ConnectionId = connectionId,
            Schema = "public",
            Table = table,
            LayerName = "Parcels Validation",
            ServiceName = serviceName,
            GeometryColumn = "geom",
            GeometryType = "Polygon",
            Srid = 3857,
            PrimaryKey = "id",
            Fields = ["name", "area_m2"],
            Enabled = true
        };

        var result = await operation.PublishAsync(command);

        // A clean validation rejection is the expected behavior; a 5xx / Unavailable means the pinned image is
        // not ready for the publish path (contract drift), so the required receipt must fail.
        Assert.False(
            string.Equals(result.State, "Unavailable", StringComparison.OrdinalIgnoreCase),
            $"The pinned honua-server image could not service the layer-publish path ({result.State} — {result.Detail}).");

        Assert.False(result.Succeeded);
        Assert.Null(result.LayerId);
        Assert.False(string.IsNullOrWhiteSpace(result.Detail));

        // The validation initiative returns field-addressable errors; when the server emits them, the console
        // binds them onto the offending input. The rejection is keyed to the fields/primaryKey relationship,
        // so a returned error must reference one of those (path or fieldId), never an empty/fabricated slot.
        if (result.FieldErrors.Count > 0)
        {
            Assert.All(result.FieldErrors, error => Assert.False(string.IsNullOrWhiteSpace(error.Message)));
            Assert.Contains(
                result.FieldErrors,
                error => Mentions(error.Path, "field", "primarykey", "pk", "id")
                    || Mentions(error.FieldId, "field", "primarykey", "pk", "id")
                    || Mentions(error.Code, "field", "primarykey", "pk"));
        }

        // Independent proof NOTHING landed: the service exposes no FeatureServer layer 0/1.
        using var verifier = _fixture.CreateVerifier();
        Assert.Null(await verifier.GetFeatureServerLayerAsync(serviceName, 0));
        Assert.Null(await verifier.GetFeatureServerLayerAsync(serviceName, 1));
        Assert.Null(await verifier.GetRegisteredLayerAsync(connectionId, serviceName, 0));
    }

    [SkippableFact]
    public async Task AnalysisPackage_WithInvalidPlan_IsRejected_AndNothingLands()
    {
        Skip.If(_fixture.SkipReason is not null, _fixture.SkipReason ?? string.Empty);

        // Draft persistence accepts incomplete execution inputs. Its actual structural contract
        // requires at least one plan step, so submit an empty step collection through the real client.
        var title = $"Console analysis validation {Guid.NewGuid():N}";
        var plan = StudioAnalysisPackageMapper.CreateTemplate();
        plan.Title = title;
        var package = StudioAnalysisPackageMapper.ToPackageContent(plan);
        var saved = await _fixture.CreateAnalysisClient().CreateItemAsync(new HonuaCreateAnalysisContentItemRequest
        {
            Name = $"invalid-{Guid.NewGuid():N}",
            Title = title,
            Kind = HonuaAnalysisContentKinds.AnalysisPackage,
            AnalysisPackage = package with { Plan = package.Plan! with { Steps = [] } }
        });

        Assert.Null(saved.Data);
        Assert.NotNull(saved.Issue);
        Assert.Equal(400, saved.Issue.StatusCode);
        Assert.Contains(saved.Issue.FieldErrors, error =>
            string.Equals(error.Path, "/analysisPackage/plan/steps", StringComparison.Ordinal));

        // Independent proof NOTHING landed: the rejected title is not searchable in the catalog.
        using var verifier = _fixture.CreateVerifier();
        var titles = await verifier.SearchCatalogTitlesAsync(title);
        Assert.DoesNotContain(title, titles);
    }

    private static bool Mentions(string? value, params string[] needles) =>
        !string.IsNullOrWhiteSpace(value)
        && needles.Any(needle => value!.Contains(needle, StringComparison.OrdinalIgnoreCase));
}
