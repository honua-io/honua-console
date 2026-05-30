using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.Native.Core.Tests;

/// <summary>
/// Behavior coverage for <see cref="HonuaServerShareAccessDataSource"/> and
/// <see cref="UnsupportedShareAccessDataSource"/> driving a fake <see cref="IHonuaConsoleShareClient"/>
/// (no HTTP, no Docker). Asserts the edge/failure paths the Share panel depends on: a blocked public
/// change re-previews the dependency closure into structured conflicts; a mint surfaces the one-time
/// secret AND reloads the refreshed projection; revoke reloads; a transport issue becomes a capability
/// state; and the unbound source always renders missing-binding rather than fabricating share data.
/// </summary>
public sealed class ShareAccessDataSourceTests
{
    private static HonuaConsoleShareProjection Projection(
        string tier = "private",
        bool publicLinkEnabled = false,
        bool embedEnabled = false,
        params string[] permissions) =>
        new()
        {
            ItemId = "item-1",
            ItemName = "storm-layer",
            ItemTitle = "Storm Layer",
            ItemType = "layer",
            AccessTier = tier,
            PublicLinkEnabled = publicLinkEnabled,
            EmbedEnabled = embedEnabled,
            OpenDataEligible = string.Equals(tier, "public-indexed", StringComparison.Ordinal),
            AnonymousEligible = HonuaConsoleShareAccessTiers.IsPublic(tier),
            CallerPermissions = permissions.Length == 0 ? ["view", "share", "embed", "administer"] : permissions
        };

    [Fact]
    public async Task Load_MapsProjectionPermissionsAndTokens()
    {
        var projection = Projection(tier: "public-link", publicLinkEnabled: true);
        var withTokens = projection with
        {
            PublicLinkTokens =
            [
                new HonuaConsolePublicLinkToken { TokenId = "tok-old", ItemId = "item-1", CreatedAt = DateTimeOffset.UtcNow.AddDays(-2), IsExpired = true },
                new HonuaConsolePublicLinkToken { TokenId = "tok-new", ItemId = "item-1", CreatedAt = DateTimeOffset.UtcNow, IsExpired = false }
            ]
        };
        var source = new HonuaServerShareAccessDataSource(new FakeShareClient { ShareProjection = withTokens });

        var load = await source.LoadAsync("item-1");

        Assert.True(load.HasShare);
        Assert.True(load.Share!.IsPublic);
        Assert.True(load.Share.CanShare);
        Assert.True(load.Share.CanEmbed);
        Assert.True(load.Share.CanAdminister);
        // Tokens are ordered newest-first.
        Assert.Equal("tok-new", load.Share.PublicLinkTokens[0].TokenId);
    }

    [Fact]
    public async Task UpdateAccessTier_WhenBlockedByClosure_RePreviewsAndReturnsStructuredConflicts()
    {
        var client = new FakeShareClient
        {
            AccessResult = HonuaAdminEndpointResult<HonuaConsoleShareProjection>.FromIssue(
                new HonuaAdminEndpointIssue("Conflict", "PUT .../access", "Dependency closure is not shareable.", 409)),
            DependencyResult = HonuaAdminEndpointResult<HonuaConsoleShareDependencyClosure>.FromData(
                new HonuaConsoleShareDependencyClosure
                {
                    ItemId = "item-1",
                    TargetTier = "public-indexed",
                    IsCompatible = false,
                    Conflicts =
                    [
                        new HonuaConsoleShareDependencyConflict { ItemId = "dep-1", ItemName = "Private source", BlockingReason = "item visibility is 'personal'" }
                    ]
                })
        };
        var source = new HonuaServerShareAccessDataSource(client);

        var result = await source.UpdateAccessTierAsync("item-1", "public-indexed", allowDependencyConflicts: false);

        Assert.False(result.Succeeded);
        Assert.NotNull(result.BlockedBy);
        Assert.False(result.BlockedBy!.IsCompatible);
        var conflict = Assert.Single(result.BlockedBy.Conflicts);
        Assert.Equal("Private source", conflict.ItemName);
        Assert.Equal("public-indexed", client.LastPreviewTier);
    }

    [Fact]
    public async Task UpdateAccessTier_OnSuccess_ReturnsRefreshedProjection()
    {
        var client = new FakeShareClient
        {
            AccessResult = HonuaAdminEndpointResult<HonuaConsoleShareProjection>.FromData(Projection(tier: "public-indexed"))
        };
        var source = new HonuaServerShareAccessDataSource(client);

        var result = await source.UpdateAccessTierAsync("item-1", "public-indexed", allowDependencyConflicts: false);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Share);
        Assert.Equal("public-indexed", result.Share!.AccessTier);
        Assert.True(result.Share.OpenDataEligible);
    }

    [Fact]
    public async Task MintPublicLink_SurfacesOneTimeSecretAndReloadsProjection()
    {
        var client = new FakeShareClient
        {
            ShareProjection = Projection(tier: "public-link", publicLinkEnabled: true),
            MintLinkResult = HonuaAdminEndpointResult<HonuaConsolePublicLinkToken>.FromData(
                new HonuaConsolePublicLinkToken { TokenId = "tok-1", Token = "secret-once", ItemId = "item-1" })
        };
        var source = new HonuaServerShareAccessDataSource(client);

        var result = await source.MintPublicLinkAsync("item-1", expiresAt: null);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.MintedSecret);
        Assert.Equal("public-link", result.MintedSecret!.Kind);
        Assert.Equal("secret-once", result.MintedSecret.Token);
        // Projection was reloaded after mint.
        Assert.NotNull(result.Share);
        Assert.True(client.GetShareCalls >= 1);
    }

    [Fact]
    public async Task MintEmbedToken_SurfacesAudienceAndExpiry()
    {
        var expires = DateTimeOffset.UtcNow.AddHours(1);
        var client = new FakeShareClient
        {
            ShareProjection = Projection(tier: "private", embedEnabled: true),
            MintEmbedResult = HonuaAdminEndpointResult<HonuaConsoleEmbedToken>.FromData(
                new HonuaConsoleEmbedToken { TokenId = "e-1", Token = "embed-secret", ItemId = "item-1", Audience = "map", ExpiresAt = expires })
        };
        var source = new HonuaServerShareAccessDataSource(client);

        var result = await source.MintEmbedTokenAsync("item-1", "map", ttlSeconds: 3600);

        Assert.True(result.Succeeded);
        Assert.Equal("embed", result.MintedSecret!.Kind);
        Assert.Equal("embed-secret", result.MintedSecret.Token);
        Assert.Equal("map", result.MintedSecret.Audience);
        Assert.Equal(expires, result.MintedSecret.ExpiresAt);
    }

    [Fact]
    public async Task RevokePublicLink_OnIssue_ReturnsCapabilityState()
    {
        var client = new FakeShareClient
        {
            RevokeResult = HonuaAdminEndpointResult<HonuaConsoleShareCommandAck>.FromIssue(
                new HonuaAdminEndpointIssue("Unsupported", "DELETE .../link/{tokenId}", "Public-link token not found.", 404))
        };
        var source = new HonuaServerShareAccessDataSource(client);

        var result = await source.RevokePublicLinkAsync("item-1", "tok-missing");

        Assert.False(result.Succeeded);
        var state = Assert.Single(result.CapabilityStates);
        Assert.Equal("Unsupported", state.State);
        Assert.Contains("not found", state.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PreviewDependencies_WhenCompatible_ReturnsCompatibleClosure()
    {
        var client = new FakeShareClient
        {
            DependencyResult = HonuaAdminEndpointResult<HonuaConsoleShareDependencyClosure>.FromData(
                new HonuaConsoleShareDependencyClosure { ItemId = "item-1", TargetTier = "public-link", IsCompatible = true, Conflicts = [] })
        };
        var source = new HonuaServerShareAccessDataSource(client);

        var result = await source.PreviewDependenciesAsync("item-1", "public-link");

        // A preview carries the closure on BlockedBy; the page reads IsCompatible to decide OK vs blocked.
        Assert.Empty(result.CapabilityStates);
        Assert.NotNull(result.BlockedBy);
        Assert.True(result.BlockedBy!.IsCompatible);
    }

    [Fact]
    public async Task Unsupported_AlwaysRendersMissingBinding()
    {
        var source = new UnsupportedShareAccessDataSource();

        var load = await source.LoadAsync("item-1");
        var mint = await source.MintPublicLinkAsync("item-1", null);

        Assert.False(load.HasShare);
        Assert.Equal("Missing binding", Assert.Single(load.CapabilityStates).State);
        Assert.False(mint.Succeeded);
        Assert.Equal("Missing binding", Assert.Single(mint.CapabilityStates).State);
    }

    private sealed class FakeShareClient : IHonuaConsoleShareClient
    {
        public Uri BaseUri { get; } = new("https://honua.test");

        public HonuaConsoleShareProjection ShareProjection { get; set; } = new() { ItemId = "item-1", ItemName = "n", ItemType = "layer" };
        public HonuaAdminEndpointResult<HonuaConsoleShareProjection>? AccessResult { get; set; }
        public HonuaAdminEndpointResult<HonuaConsoleShareProjection>? EmbedResult { get; set; }
        public HonuaAdminEndpointResult<HonuaConsoleShareDependencyClosure>? DependencyResult { get; set; }
        public HonuaAdminEndpointResult<HonuaConsolePublicLinkToken>? MintLinkResult { get; set; }
        public HonuaAdminEndpointResult<HonuaConsoleEmbedToken>? MintEmbedResult { get; set; }
        public HonuaAdminEndpointResult<HonuaConsoleShareCommandAck>? RevokeResult { get; set; }

        public int GetShareCalls { get; private set; }
        public string? LastPreviewTier { get; private set; }

        public Task<HonuaAdminEndpointResult<HonuaConsoleShareProjection>> GetShareAsync(string itemId, CancellationToken cancellationToken = default)
        {
            GetShareCalls++;
            return Task.FromResult(HonuaAdminEndpointResult<HonuaConsoleShareProjection>.FromData(ShareProjection));
        }

        public Task<HonuaAdminEndpointResult<HonuaConsoleShareProjection>> UpdateAccessTierAsync(string itemId, HonuaUpdateShareAccessRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(AccessResult ?? HonuaAdminEndpointResult<HonuaConsoleShareProjection>.FromData(ShareProjection));

        public Task<HonuaAdminEndpointResult<HonuaConsoleShareDependencyClosure>> PreviewDependenciesAsync(string itemId, string targetTier, CancellationToken cancellationToken = default)
        {
            LastPreviewTier = targetTier;
            return Task.FromResult(DependencyResult ?? HonuaAdminEndpointResult<HonuaConsoleShareDependencyClosure>.FromData(
                new HonuaConsoleShareDependencyClosure { ItemId = itemId, TargetTier = targetTier, IsCompatible = true, Conflicts = [] }));
        }

        public Task<HonuaAdminEndpointResult<HonuaConsolePublicLinkList>> ListPublicLinksAsync(string itemId, CancellationToken cancellationToken = default) =>
            Task.FromResult(HonuaAdminEndpointResult<HonuaConsolePublicLinkList>.FromData(new HonuaConsolePublicLinkList { ItemId = itemId }));

        public Task<HonuaAdminEndpointResult<HonuaConsolePublicLinkToken>> MintPublicLinkAsync(string itemId, HonuaMintPublicLinkRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(MintLinkResult ?? HonuaAdminEndpointResult<HonuaConsolePublicLinkToken>.FromData(new HonuaConsolePublicLinkToken { TokenId = "t", ItemId = itemId }));

        public Task<HonuaAdminEndpointResult<HonuaConsoleShareCommandAck>> RevokePublicLinkAsync(string itemId, string tokenId, CancellationToken cancellationToken = default) =>
            Task.FromResult(RevokeResult ?? HonuaAdminEndpointResult<HonuaConsoleShareCommandAck>.FromData(new HonuaConsoleShareCommandAck()));

        public Task<HonuaAdminEndpointResult<HonuaConsoleShareProjection>> SetEmbedAsync(string itemId, HonuaSetEmbedRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(EmbedResult ?? HonuaAdminEndpointResult<HonuaConsoleShareProjection>.FromData(ShareProjection));

        public Task<HonuaAdminEndpointResult<HonuaConsoleEmbedToken>> MintEmbedTokenAsync(string itemId, HonuaMintEmbedTokenRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(MintEmbedResult ?? HonuaAdminEndpointResult<HonuaConsoleEmbedToken>.FromData(new HonuaConsoleEmbedToken { TokenId = "e", ItemId = itemId, Audience = request.Audience }));
    }
}
