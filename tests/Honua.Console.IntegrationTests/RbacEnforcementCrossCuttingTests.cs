using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Cross-cutting RBAC-enforcement round-trip (console-integration-test-plan.md §5.1, Wave 6 — the final
/// cross-cutting suite).
///
/// Proves a forbidden/unauthorized mutation is BLOCKED server-side — not merely behind a UI gate — and that
/// the server state did NOT change as a result. The console mutation OPERATION is driven through an
/// UNAUTHENTICATED client (no admin key) so the rejection comes from the server's authorization layer; the
/// resulting state is then read back INDEPENDENTLY through the <see cref="ServerStateVerifier"/> oracle (with
/// admin credentials) to confirm nothing landed. Uses the 401/403-vs-404 route-mounted discriminator from
/// Wave 3 (<see cref="ServerStateVerifier.ProbeAnonymousMutationStatusAsync"/>): a gated mutation rejects an
/// anonymous request with 401/403 while an absent route returns 404, so a missing route never masquerades as
/// an enforced denial.
///
/// Off by default; the SkippableFacts skip cleanly without Docker / the opt-in env (Console Patterns Charter
/// section 11) and RUN in the nightly lane (.github/workflows/console-nightly.yml).
/// </summary>
[Collection(CrossCuttingIntegrationCollection.Name)]
public sealed class RbacEnforcementCrossCuttingTests
{
    private readonly CrossCuttingFixture _fixture;

    public RbacEnforcementCrossCuttingTests(CrossCuttingFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task UnauthorizedLayerPublish_IsBlocked_AndNothingLands()
    {
        Skip.If(_fixture.SkipReason is not null, _fixture.SkipReason ?? string.Empty);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var table = $"parcels_rbac_{suffix}";
        var serviceName = $"parcels_rbac_svc_{suffix}";
        await CrossCuttingSeeder.SeedPolygonTableAsync(_fixture.PostgresConnectionString!, table);
        // The connection is seeded WITH admin auth (a valid source exists); the forbidden act is the publish.
        var connectionId = await CrossCuttingSeeder.CreateConnectionAsync(_fixture, $"parcels-rbac-conn-{suffix}");

        using var verifier = _fixture.CreateVerifier();

        // --- Route-mounted discriminator: the layer-publish mutation rejects a genuinely anonymous POST with
        //     401/403 (gated), NOT 404 (absent). This proves enforcement is real and the route exists. ---
        var anonStatus = await verifier.ProbeAnonymousMutationStatusAsync(
            HttpMethod.Post,
            $"/api/v1/admin/connections/{Uri.EscapeDataString(connectionId)}/layers/",
            jsonBody: "{}");
        Skip.If(
            anonStatus == 0,
            "The honua-server admin layer-publish route could not be reached; the pinned image is not ready for the RBAC round-trip.");
        Skip.If(
            anonStatus == 404,
            "The pinned honua-server image does not mount the admin layer-publish route (404); the RBAC enforcement "
            + "round-trip needs a server build whose layer-publishing path is present.");
        Assert.True(
            anonStatus is 401 or 403,
            $"An unauthenticated layer-publish mutation was not rejected with 401/403 (got HTTP {anonStatus}); the mutation is not gated server-side.");

        // --- OPERATION under test through the UNAUTHENTICATED console client: a known-valid publish that
        //     would succeed with admin auth must be blocked by the server's authorization layer. ---
        var unauthOperation = new HonuaServerServiceLayerPublishOperation(_fixture.CreateUnauthenticatedOperateClient());
        var command = new ServiceLayerPublishCommand
        {
            ConnectionId = connectionId,
            Schema = "public",
            Table = table,
            LayerName = "Parcels RBAC",
            ServiceName = serviceName,
            GeometryColumn = "geom",
            GeometryType = "Polygon",
            Srid = 3857,
            PrimaryKey = "id",
            Fields = ["id", "name", "area_m2"],
            Enabled = true
        };

        var blocked = await unauthOperation.PublishAsync(command);
        Assert.False(blocked.Succeeded, "An unauthenticated layer-publish operation was unexpectedly accepted.");
        Assert.Null(blocked.LayerId);
        // The console surfaces the shared "Missing permission" state vocabulary token for an auth denial — not
        // a fabricated success and not the generic missing-binding placeholder.
        Assert.Equal("Missing permission", blocked.State);

        // --- Independent proof the op TRULY did not happen: an admin read of the registry / FeatureServer
        //     finds no layer for the service the forbidden publish targeted. ---
        Assert.Null(await verifier.GetRegisteredLayerAsync(connectionId, serviceName, 0));
        Assert.Null(await verifier.GetFeatureServerLayerAsync(serviceName, 0));
        Assert.Null(await verifier.GetFeatureServerLayerAsync(serviceName, 1));

        // --- Conversely, the ALLOWED (admin) principal succeeds against the same source — proving the denial
        //     above was an authorization decision, not a broken request. (Skips if the publish path is not
        //     ready on the pinned image; the negative enforcement proof above still stands.) ---
        var adminOperation = new HonuaServerServiceLayerPublishOperation(_fixture.CreateOperateClient());
        var allowed = await adminOperation.PublishAsync(command);
        Skip.If(
            !allowed.Succeeded
            && (string.Equals(allowed.State, "Unavailable", StringComparison.OrdinalIgnoreCase)
                || (allowed.Detail?.Contains("HTTP 5", StringComparison.OrdinalIgnoreCase) ?? false)
                || (allowed.Detail?.Contains("500", StringComparison.Ordinal) ?? false)),
            $"The pinned honua-server image could not service the admin layer-publish path ({allowed.State} — {allowed.Detail}); "
            + "the allowed-scope half of the RBAC round-trip needs a server build whose layer-publishing path is ready.");
        Assert.True(allowed.Succeeded, $"The admin layer-publish was rejected: {allowed.State} — {allowed.Detail}");
        Assert.NotNull(allowed.LayerId);

        var landed = await verifier.GetRegisteredLayerAsync(connectionId, serviceName, allowed.LayerId!.Value);
        Assert.NotNull(landed);
        Assert.Equal("Parcels RBAC", landed!.LayerName);
    }

    [SkippableFact]
    public async Task UnauthorizedContentPublish_IsBlocked_AndNoRouteLands()
    {
        Skip.If(_fixture.SkipReason is not null, _fixture.SkipReason ?? string.Empty);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var slug = $"console-rbac-pub-{suffix}";

        using var verifier = _fixture.CreateVerifier();

        // Route-mounted discriminator first: an anonymous content-publish POST is gated (401/403), not absent.
        var anonStatus = await verifier.ProbeAnonymousMutationStatusAsync(
            HttpMethod.Post,
            "/api/v1/console/publications",
            jsonBody: "{}");
        Skip.If(
            anonStatus is 0 or 404,
            $"The honua-server content-publish route is not ready/mounted on the pinned image (HTTP {anonStatus}); the RBAC round-trip needs it present.");
        Assert.True(
            anonStatus is 401 or 403,
            $"An unauthenticated content-publish mutation was not rejected with 401/403 (got HTTP {anonStatus}).");

        // The console publish OPERATION through an UNAUTHENTICATED client must be blocked, leaving no route.
        var unauthClient = new HonuaContentPublicationHttpClient(
            _fixture.CreateHttpClient(),
            new HonuaContentPublicationClientOptions(_fixture.BaseAddress, ApiKey: null));
        var request = new HonuaPublishContentRequest
        {
            Kind = HonuaContentPublicationKinds.Map,
            RouteSlug = slug,
            Title = $"Console RBAC {suffix}",
            ContentPayload = """{"map":"console-rbac"}"""
        };

        var blocked = await unauthClient.PublishAsync(request);
        Assert.Null(blocked.Data);
        Assert.NotNull(blocked.Issue);
        Assert.True(
            blocked.Issue!.StatusCode is 401 or 403,
            $"An unauthenticated content-publish returned an unexpected status ({blocked.Issue.StatusCode}).");

        // Independent proof nothing landed: the slug is not anonymously reachable.
        var anon = await verifier.FetchPublishedRouteAnonymouslyAsync(slug);
        Assert.False(anon.Granted, $"A blocked content-publish unexpectedly left an anonymously-reachable route (HTTP {anon.StatusCode}).");
    }
}
