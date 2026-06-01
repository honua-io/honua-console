using System.Net.Http.Json;
using System.Text.Json;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Share token-grants-anonymous-access round-trip (console-integration-test-plan.md Wave 2, Family D, P0).
///
/// Drives the console Share OPERATION through the real <see cref="HonuaServerShareAccessDataSource"/> (the
/// production <see cref="IShareAccessDataSource"/> over honua-server#1215): raise the access tier, mint a
/// public-link token, mint an embed token. It then asserts the resulting grant INDEPENDENTLY through the
/// <see cref="ServerStateVerifier"/> oracle hitting the ANONYMOUS Console Share surface
/// (<c>/api/v1/console/share/...</c>) with a genuinely UNAUTHENTICATED client — proving the minted token
/// actually grants access for its scope, and that an unminted/revoked token does NOT (independently
/// verified). This converts the biggest Share PARTIAL cluster (mint/revoke/access-tier asserted only via the
/// same admin projection) into a true round-trip per plan §4 rule #2.
///
/// Off by default; the SkippableFacts skip cleanly without Docker / the opt-in env (Console Patterns Charter
/// section 11) and RUN in the nightly lane (.github/workflows/console-nightly.yml).
/// </summary>
[Collection(ShareAccessIntegrationCollection.Name)]
public sealed class ShareTokenGrantsAccessRoundTripTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ShareAccessFixture _fixture;

    public ShareTokenGrantsAccessRoundTripTests(ShareAccessFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task MintedPublicLink_GrantsAnonymousAccess_WhileUnmintedAndRevokedTokensDoNot()
    {
        Skip.If(_fixture.SkipReason is not null, _fixture.SkipReason ?? string.Empty);

        var itemId = await SeedLayerItemAsync($"console-share-grant-{Guid.NewGuid():N}");
        var dataSource = new HonuaServerShareAccessDataSource(_fixture.CreateShareClient());
        using var verifier = _fixture.CreateVerifier();

        // --- Negative baseline (independent): a random, never-minted token grants NOTHING. ---
        var unminted = await verifier.ResolvePublicLinkAnonymouslyAsync(Guid.NewGuid().ToString("N"));
        Skip.If(
            unminted.StatusCode == 0,
            "The anonymous Console Share surface could not be reached; the pinned server image is not ready for the share round-trip.");
        Assert.False(unminted.Granted, $"An unminted token was unexpectedly granted (HTTP {unminted.StatusCode}).");

        // --- OPERATION under test: raise the access tier, then mint a public link through the console. ---
        var raised = await dataSource.UpdateAccessTierAsync(
            itemId,
            HonuaConsoleShareAccessTiers.PublicIndexed,
            allowDependencyConflicts: false);
        Assert.True(raised.Succeeded, $"Access-tier raise failed: {Describe(raised)}");

        var minted = await dataSource.MintPublicLinkAsync(itemId, expiresAt: null);
        Assert.True(minted.Succeeded, $"Mint failed: {Describe(minted)}");
        Assert.NotNull(minted.MintedSecret);
        var token = minted.MintedSecret!.Token;
        var tokenId = minted.MintedSecret.TokenId;
        Assert.False(string.IsNullOrWhiteSpace(token));

        // --- Independent grant proof: an UNAUTHENTICATED client resolves the minted token (200). ---
        var grant = await verifier.ResolvePublicLinkAnonymouslyAsync(token);
        Assert.True(grant.Granted, $"The minted public-link token did not grant anonymous access (HTTP {grant.StatusCode}).");
        Assert.Equal(itemId, grant.ItemId);

        // The access-tier raise also makes the item resolvable anonymously by id (public-indexed grant).
        var publicContent = await verifier.ResolvePublicContentAnonymouslyAsync(itemId);
        Assert.True(publicContent.Granted, $"Public-indexed content was not anonymously resolvable (HTTP {publicContent.StatusCode}).");
        Assert.Equal(itemId, publicContent.ItemId);

        // --- Negative companion: revoke the token → the same anonymous resolve is now DENIED. ---
        var revoked = await dataSource.RevokePublicLinkAsync(itemId, tokenId);
        Assert.True(revoked.Succeeded, $"Revoke failed: {Describe(revoked)}");

        var afterRevoke = await verifier.ResolvePublicLinkAnonymouslyAsync(token);
        Assert.False(
            afterRevoke.Granted,
            $"A revoked public-link token still granted anonymous access (HTTP {afterRevoke.StatusCode}).");
    }

    [SkippableFact]
    public async Task MintedEmbedToken_GrantsAnonymousEmbedRedemption()
    {
        Skip.If(_fixture.SkipReason is not null, _fixture.SkipReason ?? string.Empty);

        var itemId = await SeedLayerItemAsync($"console-embed-grant-{Guid.NewGuid():N}");
        var dataSource = new HonuaServerShareAccessDataSource(_fixture.CreateShareClient());
        using var verifier = _fixture.CreateVerifier();

        // --- Negative baseline (independent): a never-minted embed token redeems to nothing. ---
        var unminted = await verifier.RedeemEmbedTokenAnonymouslyAsync(Guid.NewGuid().ToString("N"));
        Skip.If(
            unminted.StatusCode == 0,
            "The anonymous Console Share embed surface could not be reached; the pinned server image is not ready for the embed round-trip.");
        Assert.False(unminted.Granted, $"An unminted embed token was unexpectedly redeemed (HTTP {unminted.StatusCode}).");

        // --- OPERATION under test: enable embedding, then mint an embed token through the console. ---
        var enabled = await dataSource.SetEmbedAsync(
            itemId,
            enabled: true,
            HonuaConsoleEmbedAudiences.Content,
            allowDependencyConflicts: false);
        Assert.True(enabled.Succeeded, $"Embed enable failed: {Describe(enabled)}");

        var minted = await dataSource.MintEmbedTokenAsync(itemId, HonuaConsoleEmbedAudiences.Content, ttlSeconds: 3600);
        Assert.True(minted.Succeeded, $"Embed mint failed: {Describe(minted)}");
        Assert.NotNull(minted.MintedSecret);
        var token = minted.MintedSecret!.Token;
        Assert.False(string.IsNullOrWhiteSpace(token));

        // --- Independent grant proof: an UNAUTHENTICATED client redeems the minted embed token (200). ---
        var redeem = await verifier.RedeemEmbedTokenAnonymouslyAsync(token);
        Assert.True(redeem.Granted, $"The minted embed token did not grant anonymous redemption (HTTP {redeem.StatusCode}).");
        Assert.Equal(itemId, redeem.ItemId);
    }

    [SkippableFact]
    public async Task PrivateItem_IsNotAnonymouslyResolvable()
    {
        Skip.If(_fixture.SkipReason is not null, _fixture.SkipReason ?? string.Empty);

        // A freshly-seeded item is private; without any access-tier raise it must NOT resolve anonymously.
        var itemId = await SeedLayerItemAsync($"console-private-{Guid.NewGuid():N}");
        using var verifier = _fixture.CreateVerifier();

        var anon = await verifier.ResolvePublicContentAnonymouslyAsync(itemId);
        Skip.If(
            anon.StatusCode == 0,
            "The anonymous Console Share content surface could not be reached; the pinned server image is not ready.");
        Assert.False(anon.Granted, $"A private item was anonymously resolvable (HTTP {anon.StatusCode}).");
    }

    private async Task<string> SeedLayerItemAsync(string name)
    {
        using var client = _fixture.CreateRawClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/console/content")
        {
            Content = JsonContent.Create(new
            {
                name,
                itemType = "layer",
                title = "Console share grant fixture layer",
                visibility = "personal"
            }, options: JsonOptions)
        };
        if (!string.IsNullOrWhiteSpace(_fixture.AdminApiKey))
        {
            request.Headers.TryAddWithoutValidation("X-API-Key", _fixture.AdminApiKey);
        }

        using var response = await client.SendAsync(request);
        var raw = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"Seeding the content item failed (HTTP {(int)response.StatusCode}): {raw}");

        using var doc = JsonDocument.Parse(raw);
        var id = doc.RootElement.GetProperty("data").GetProperty("id").GetString();
        Assert.False(string.IsNullOrWhiteSpace(id), "The seeded content item had no id.");
        return id!;
    }

    private static string Describe(ShareCommandResult result) =>
        result.BlockedBy is { } blocked
            ? $"blocked by closure for {blocked.TargetTier}: {string.Join("; ", blocked.Conflicts.Select(c => c.BlockingReason))}"
            : result.CapabilityStates.Count == 0
                ? "<no capability states>"
                : string.Join("; ", result.CapabilityStates.Select(s => $"{s.State}: {s.Detail}"));
}
