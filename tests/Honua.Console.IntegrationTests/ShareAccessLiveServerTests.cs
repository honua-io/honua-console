using System.Net.Http.Json;
using System.Text.Json;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Proves the server-bound Share management surface renders from live honua-server data (AC: the Share
/// surface renders from a real honua-server started via Testcontainers and seeded with a known fixture; the
/// in-memory client is removed from the merged build). Seeds a known Console content item through the real
/// content API, then drives <see cref="HonuaServerShareAccessDataSource"/> over the real Console Share
/// access lifecycle (honua-server#1215): read the projection, raise the access tier to public-indexed, mint
/// and revoke a public-link token, and enable embedding. Docker-unavailable environments skip cleanly.
/// </summary>
[Collection(ShareAccessIntegrationCollection.Name)]
public sealed class ShareAccessLiveServerTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ShareAccessFixture _fixture;

    public ShareAccessLiveServerTests(ShareAccessFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task ShareAccess_SeedsItemAndDrivesShareLifecycleFromLiveServer()
    {
        Skip.If(_fixture.SkipReason is not null, _fixture.SkipReason ?? string.Empty);

        // 1. Seed a known content item (a layer is a distributable / open-data eligible type).
        var itemId = await SeedLayerItemAsync($"console-share-fixture-{Guid.NewGuid():N}");

        var dataSource = new HonuaServerShareAccessDataSource(_fixture.CreateShareClient());

        // 2. The Share projection reads from live data (not a mock) — defaults to private/no public link.
        var load = await dataSource.LoadAsync(itemId);
        Assert.DoesNotContain(load.CapabilityStates, state => state.State == "Missing binding");
        Assert.True(load.HasShare, $"Share projection was not returned: {Describe(load.CapabilityStates)}");
        Assert.Equal(itemId, load.Share!.ItemId);
        Assert.False(load.Share.IsPublic);

        // 3. Raise the access tier to public-indexed (dependency-closure clean for a fresh item).
        var raised = await dataSource.UpdateAccessTierAsync(itemId, HonuaConsoleShareAccessTiers.PublicIndexed, allowDependencyConflicts: false);
        Assert.True(raised.Succeeded, $"Access tier change was blocked/failed: {Describe(raised)}");
        Assert.NotNull(raised.Share);
        Assert.True(raised.Share!.IsPublic);
        Assert.True(raised.Share.OpenDataEligible);
        Assert.True(raised.Share.AnonymousEligible);

        // 4. Mint a public-link token — the opaque value is returned exactly once.
        var minted = await dataSource.MintPublicLinkAsync(itemId, expiresAt: null);
        Assert.True(minted.Succeeded, $"Mint failed: {Describe(minted)}");
        Assert.NotNull(minted.MintedSecret);
        Assert.False(string.IsNullOrWhiteSpace(minted.MintedSecret!.Token));
        var tokenId = minted.MintedSecret.TokenId;

        // The reloaded projection shows the active token, with its value redacted.
        Assert.NotNull(minted.Share);
        var listed = Assert.Single(minted.Share!.PublicLinkTokens, token => token.TokenId == tokenId);
        Assert.False(listed.IsExpired);

        // 5. Revoke the token; the projection no longer reports it as active.
        var revoked = await dataSource.RevokePublicLinkAsync(itemId, tokenId);
        Assert.True(revoked.Succeeded, $"Revoke failed: {Describe(revoked)}");
        Assert.DoesNotContain(revoked.Share!.PublicLinkTokens, token => token.TokenId == tokenId && !token.IsExpired);

        // 6. Enable embedding for the map/content audience.
        var embed = await dataSource.SetEmbedAsync(itemId, enabled: true, HonuaConsoleEmbedAudiences.Content, allowDependencyConflicts: false);
        Assert.True(embed.Succeeded, $"Embed enable failed: {Describe(embed)}");
        Assert.True(embed.Share!.EmbedEnabled);
        Assert.Equal(HonuaConsoleEmbedAudiences.Content, embed.Share.EmbedAudience);
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
                title = "Console share fixture layer",
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
            : Describe(result.CapabilityStates);

    private static string Describe(IReadOnlyList<ShareCapabilityState> states) =>
        states.Count == 0 ? "<no capability states>" : string.Join("; ", states.Select(s => $"{s.State}: {s.Detail}"));
}
