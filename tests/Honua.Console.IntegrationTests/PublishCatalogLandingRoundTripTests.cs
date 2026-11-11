using Honua.Console.Contracts;

namespace Honua.Console.IntegrationTests;

[Collection(ContentPublicationIntegrationCollection.Name)]
public sealed class PublishCatalogLandingRoundTripTests
{
    private readonly ContentPublicationFixture _fixture;

    public PublishCatalogLandingRoundTripTests(ContentPublicationFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task PublicPublish_LandsAtRouteWithVisibility_AndIsAnonymouslyReachable()
    {
        Skip.If(_fixture.SkipReason is not null, _fixture.SkipReason ?? string.Empty);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var slug = $"console-it-map-{suffix}";
        var title = $"Console IT Map {suffix}";

        var client = _fixture.CreateContentPublicationClient();
        var request = new HonuaPublishContentRequest
        {
            Kind = HonuaContentPublicationKinds.Map,
            RouteSlug = slug,
            Title = title,
            ContentPayload = """{"map":"console-integration-test"}""",
            // Public + anonymous so the published route is independently reachable by an unauthenticated client.
            Policy = new HonuaContentPublicationPolicy
            {
                Visibility = HonuaContentPublicationVisibilities.Public,
                Share = new HonuaContentSharePolicy { AllowSharing = true, AllowAnonymous = true }
            }
        };

        var result = await client.PublishAsync(request);
        RequireContentPublish(result);
        Assert.NotNull(result.Data);
        var publicationId = result.Data!.Route.PublicationId;
        Assert.False(string.IsNullOrWhiteSpace(publicationId));

        using var verifier = _fixture.CreateVerifier();

        // 1. Admin publication detail (independent of the publish response object) reflects the landed route.
        var route = await verifier.GetPublicationRouteAsync(publicationId);
        Assert.NotNull(route);
        Assert.Equal(slug, route!.RouteSlug);
        Assert.Equal(HonuaContentPublicationKinds.Map, route.Kind);
        Assert.Equal(1, route.ActiveRevision);
        Assert.Equal(HonuaContentPublicationVisibilities.Public, route.Visibility);

        // 2. The published artifact is reachable ANONYMOUSLY at its slug (the round-trip proof it is live).
        var anon = await verifier.FetchPublishedRouteAnonymouslyAsync(route.RouteSlug!);
        RequireAnonymousRoute(anon);
        Assert.True(anon.Granted, $"The public publication was not anonymously reachable at its slug (HTTP {anon.StatusCode}).");
    }

    [SkippableFact]
    public async Task PrivatePublish_LandsButIsNotAnonymouslyReachable()
    {
        Skip.If(_fixture.SkipReason is not null, _fixture.SkipReason ?? string.Empty);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var slug = $"console-it-private-{suffix}";

        var client = _fixture.CreateContentPublicationClient();
        var request = new HonuaPublishContentRequest
        {
            Kind = HonuaContentPublicationKinds.Report,
            RouteSlug = slug,
            Title = $"Console IT Private {suffix}",
            ContentPayload = """{"report":"console-integration-test"}""",
            Policy = new HonuaContentPublicationPolicy
            {
                Visibility = HonuaContentPublicationVisibilities.Private,
                Share = new HonuaContentSharePolicy { AllowSharing = false, AllowAnonymous = false }
            }
        };

        var result = await client.PublishAsync(request);
        RequireContentPublish(result);
        Assert.NotNull(result.Data);

        using var verifier = _fixture.CreateVerifier();

        // The route lands and the AUTHORITATIVE independent admin read shows private visibility — the
        // server-owned, server-normalized visibility (rule #2: a DIFFERENT read API than the publish path).
        var route = await verifier.GetPublicationRouteAsync(result.Data!.Route.PublicationId);
        Assert.NotNull(route);
        Assert.Equal(slug, route!.RouteSlug);
        Assert.Equal(HonuaContentPublicationVisibilities.Private, route.Visibility);

        // An UNAUTHENTICATED client must not be able to read a private route. NOTE: the nightly lane runs the
        // server with dev-auth bypass (HONUA_DEV_AUTH_ALLOW_BYPASS=true), which auto-authenticates EVERY
        // request as admin on the auth-gated /published surface — so a 200 there does not prove a leak, only
        // that the probe was treated as an admin. The denial assertion is therefore only meaningful when the
        // probe is genuinely anonymous (no bypass); skip cleanly otherwise rather than asserting a false
        // result. The private visibility above is the authoritative independent landing proof regardless.
        var anon = await verifier.FetchPublishedRouteAnonymouslyAsync(route.RouteSlug!);
        RequireAnonymousRoute(anon);
        // Only the dev-auth-bypass profile can excuse a 2xx here (the probe is auto-authenticated as admin).
        // Gate the skip on that explicit signal so a genuine anonymous leak still FAILS in a non-bypass run,
        // rather than being silently masked. (Codex review #147.)
        Skip.If(
            anon.Granted && _fixture.DevAuthBypassEnabled,
            "The server runs with dev-auth bypass (HONUA_DEV_AUTH_ALLOW_BYPASS), which auto-authenticates the "
            + "probe, so anonymous denial of a private published route cannot be proven here; the authoritative "
            + "private-visibility admin read above is the landing proof.");
        Assert.False(anon.Granted, $"A private publication was anonymously reachable (HTTP {anon.StatusCode}).");
    }

    [SkippableFact]
    public async Task InvalidPublish_IsRejected_AndNothingLands()
    {
        Skip.If(_fixture.SkipReason is not null, _fixture.SkipReason ?? string.Empty);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var slug = $"console-it-bad-{suffix}";

        var client = _fixture.CreateContentPublicationClient();

        // Invalid config: an inverted default-view bbox (minX > maxX) is rejected deterministically by the
        // server's publish validation (defaultViewBbox minimum must not exceed maximum) — ties to the
        // validation initiative. No route is claimed for the slug.
        var request = new HonuaPublishContentRequest
        {
            Kind = HonuaContentPublicationKinds.Map,
            RouteSlug = slug,
            Title = $"Console IT Bad {suffix}",
            ContentPayload = """{"map":"console-integration-test-invalid"}""",
            DefaultViewBbox = new HonuaContentPublicationBbox
            {
                Crs = "EPSG:4326",
                MinX = 10,
                MinY = 10,
                MaxX = -10,
                MaxY = -10
            }
        };

        var result = await client.PublishAsync(request);

        Assert.False(
            result.Issue is { } issue5 && issue5.StatusCode >= 500,
            $"The pinned honua-server image could not service the publish path ({result.Issue?.State}).");

        Assert.Null(result.Data);
        Assert.NotNull(result.Issue);
        Assert.Equal("Rejected", result.Issue!.State);

        // Independently confirm NOTHING landed: no publication is reachable at the slug, anonymously or not.
        using var verifier = _fixture.CreateVerifier();
        var anon = await verifier.FetchPublishedRouteAnonymouslyAsync(slug);
        RequireAnonymousRoute(anon);
        Assert.False(anon.Granted, $"A rejected publish unexpectedly left an anonymously-reachable route (HTTP {anon.StatusCode}).");
    }

    // Status 0 means the lane never reached the server. A 5xx is a failed receipt, not a denial.
    private static void RequireAnonymousRoute(VerifiedAnonymousFetch fetch)
    {
        Skip.If(
            fetch.StatusCode == 0,
            "The anonymous published-route surface could not be reached.");
        Assert.True(
            fetch.StatusCode < 500,
            $"The anonymous published-route surface returned HTTP {fetch.StatusCode}.");
    }

    private static void RequireContentPublish(HonuaAdminEndpointResult<HonuaContentPublicationDetail> result)
    {
        if (result.Data is not null)
        {
            return;
        }

        var issue = result.Issue;
        Assert.False(
            issue is null || LiveImageReceipt.ContentIssueIsImageFailure(issue.StatusCode, issue.State),
            $"The pinned honua-server image could not service the content-publish path ({issue?.State}).");
    }
}
