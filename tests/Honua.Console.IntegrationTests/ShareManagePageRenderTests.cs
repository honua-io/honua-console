using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free render coverage for the Share management page (<c>/share/manage</c>). Verifies the
/// missing-binding surface, a loaded projection rendering an unambiguous public/private state plus the
/// public-link and embed panels gated on the caller's facets, a blocked dependency closure rendering the
/// blocking dependencies, and a minted token surfacing its one-time secret value. Drives the page through a
/// fake <see cref="IShareAccessDataSource"/> rather than a mock server; the live binding ships its own
/// opt-in Testcontainers suite.
/// </summary>
public sealed class ShareManagePageRenderTests
{
    [Fact]
    public void ShareManage_WhenBindingMissing_RendersNotBoundSurface()
    {
        var data = new FakeShareDataSource
        {
            Load = new ShareAccessLoad(null, [MissingBinding])
        };

        var page = Render(data, itemId: "item-1");

        page.WaitForAssertion(
            () => Assert.Contains("Share access API is not bound", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.DoesNotContain("data-share-access", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ShareManage_LoadedPublicItem_RendersUnambiguousStateAndPanels()
    {
        var data = new FakeShareDataSource
        {
            Load = new ShareAccessLoad(
                PublicShare(tier: "public-indexed", publicLinkEnabled: true, embedEnabled: false, canShare: true, canEmbed: true),
                [])
        };

        var page = Render(data, itemId: "item-1");

        page.WaitForAssertion(
            () => Assert.Contains("data-share-access=\"item-1\"", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Contains("data-share-tier=\"public-indexed\"", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Public", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Eligible for open-data distribution", page.Markup, StringComparison.Ordinal);
        // Per the ShareLinkConfig mockup: a detail header with status badges, a two-column layout, and a tab row.
        Assert.Contains("data-share-config", page.Markup, StringComparison.Ordinal);
        Assert.Contains("data-share-header-tier=\"public-indexed\"", page.Markup, StringComparison.Ordinal);
        Assert.Contains("data-share-layout", page.Markup, StringComparison.Ordinal);
        Assert.Contains("data-share-sidebar", page.Markup, StringComparison.Ordinal);
        Assert.Contains("data-share-tabs", page.Markup, StringComparison.Ordinal);
        foreach (var tab in new[] { "link", "embed", "opendata", "exports", "audit" })
        {
            Assert.Contains($"data-share-tab=\"{tab}\"", page.Markup, StringComparison.Ordinal);
        }
        // The default tab is the public link tab: public-link panel renders, embed/exports panels are scoped away.
        Assert.Contains("data-share-panel=\"public-link\"", page.Markup, StringComparison.Ordinal);
        Assert.Contains("data-share-token=\"tok-1\"", page.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("data-share-panel=\"embed\"", page.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("data-share-panel=\"exports\"", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ShareManage_SwitchingTabs_ScopesPanelsPerMockup()
    {
        var data = new FakeShareDataSource
        {
            Load = new ShareAccessLoad(
                PublicShare(tier: "public-indexed", publicLinkEnabled: true, embedEnabled: true, canShare: true, canEmbed: true),
                [])
        };

        var page = Render(data, itemId: "item-1");
        page.WaitForAssertion(
            () => Assert.Contains("data-share-tabs", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        // Embed tab reveals the embed panel and hides the public-link panel.
        page.Find("[data-share-tab='embed']").Click();
        page.WaitForAssertion(
            () => Assert.Contains("data-share-panel=\"embed\"", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.DoesNotContain("data-share-panel=\"public-link\"", page.Markup, StringComparison.Ordinal);

        // Exports tab deep-links into Operate jobs per the ShareExports mockup.
        page.Find("[data-share-tab='exports']").Click();
        page.WaitForAssertion(
            () => Assert.Contains("data-share-panel=\"exports\"", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Contains("data-share-exports-link", page.Markup, StringComparison.Ordinal);
        Assert.Contains("/operate/jobs", page.Markup, StringComparison.Ordinal);

        // Open-data tab surfaces the public landing entry point.
        page.Find("[data-share-tab='opendata']").Click();
        page.WaitForAssertion(
            () => Assert.Contains("data-share-panel=\"opendata\"", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Contains("data-share-opendata-link", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ShareManage_PrivateItemWithoutSharePermission_HidesShareControls()
    {
        var data = new FakeShareDataSource
        {
            Load = new ShareAccessLoad(
                PublicShare(tier: "private", publicLinkEnabled: false, embedEnabled: false, canShare: false, canEmbed: false),
                [])
        };

        var page = Render(data, itemId: "item-1");

        page.WaitForAssertion(
            () => Assert.Contains("data-share-access=\"item-1\"", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Contains("Private / membership-scoped", page.Markup, StringComparison.Ordinal);
        // No share/embed authoring panels for a caller lacking the facets.
        Assert.DoesNotContain("data-share-panel=\"access-tier\"", page.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("data-share-panel=\"public-link\"", page.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("data-share-panel=\"embed\"", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ShareManage_ChangeTierBlocked_RendersDependencyConflicts()
    {
        var data = new FakeShareDataSource
        {
            Load = new ShareAccessLoad(PublicShare(tier: "private", false, false, canShare: true, canEmbed: true), []),
            Command = new ShareCommandResult(
                null,
                null,
                new ShareDependencyClosureView(
                    "item-1",
                    "public-indexed",
                    IsCompatible: false,
                    [new ShareDependencyConflictView("dep-1", "layer", "Private upstream", "item visibility is 'personal'")]),
                [])
        };

        var page = Render(data, itemId: "item-1");
        page.WaitForAssertion(
            () => Assert.Contains("data-share-panel=\"access-tier\"", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        page.Find("[data-share-panel='access-tier'] button.console-button").Click();

        page.WaitForAssertion(
            () => Assert.Contains("data-share-blocked=\"public-indexed\"", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Contains("Private upstream", page.Markup, StringComparison.Ordinal);
        Assert.Contains("item visibility is", page.Markup, StringComparison.Ordinal);
        Assert.Contains("personal", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ShareManage_MintPublicLink_SurfacesOneTimeSecret()
    {
        var data = new FakeShareDataSource
        {
            Load = new ShareAccessLoad(PublicShare(tier: "public-link", publicLinkEnabled: false, embedEnabled: false, canShare: true, canEmbed: true), []),
            Command = new ShareCommandResult(
                PublicShare(tier: "public-link", publicLinkEnabled: true, embedEnabled: false, canShare: true, canEmbed: true),
                new ShareMintedSecret("public-link", "tok-2", "opaque-secret-once", null, null),
                null,
                [])
        };

        var page = Render(data, itemId: "item-1");
        page.WaitForAssertion(
            () => Assert.Contains("data-share-panel=\"public-link\"", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        // Submit the mint form via its submit button.
        page.Find("[data-share-panel='public-link'] button[type='submit']").Click();

        page.WaitForAssertion(
            () => Assert.Contains("data-share-minted=\"public-link\"", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Contains("opaque-secret-once", page.Markup, StringComparison.Ordinal);
        Assert.Contains("shown only once", page.Markup, StringComparison.Ordinal);
    }

    private static IRenderedComponent<ShareManagePage> Render(FakeShareDataSource data, string itemId)
    {
        var ctx = new Bunit.TestContext();
        ctx.Services.AddSingleton<IShareAccessDataSource>(data);
        var navigation = ctx.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo(navigation.GetUriWithQueryParameter("itemId", itemId));
        return ctx.RenderComponent<ShareManagePage>();
    }

    private static ShareAccessView PublicShare(string tier, bool publicLinkEnabled, bool embedEnabled, bool canShare, bool canEmbed) =>
        new(
            ItemId: "item-1",
            ItemName: "storm-layer",
            ItemTitle: "Storm Layer",
            ItemType: "layer",
            OwnerId: "owner@honua.test",
            AccessTier: tier,
            PublicLinkEnabled: publicLinkEnabled,
            EmbedEnabled: embedEnabled,
            EmbedAudience: embedEnabled ? "map" : null,
            OpenDataEligible: string.Equals(tier, "public-indexed", StringComparison.Ordinal),
            AnonymousEligible: tier.StartsWith("public", StringComparison.Ordinal),
            CanShare: canShare,
            CanEmbed: canEmbed,
            CanAdminister: true,
            PublicLinkTokens: publicLinkEnabled
                ? [new SharePublicLinkView("tok-1", DateTimeOffset.UtcNow, null, "owner@honua.test", false)]
                : [],
            UpdatedAt: DateTimeOffset.UtcNow,
            UpdatedById: "owner@honua.test");

    private static readonly ShareCapabilityState MissingBinding = new(
        "Share access",
        "Missing binding",
        "Honua:Server:BaseUrl",
        "Configure Honua:Server:BaseUrl so the Share panel can bind the server-owned Console Share access API.");

    private sealed class FakeShareDataSource : IShareAccessDataSource
    {
        public ShareAccessLoad Load { get; set; } = new(null, []);
        public ShareCommandResult Command { get; set; } = new(null, null, null, []);

        public Task<ShareAccessLoad> LoadAsync(string itemId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Load);

        public Task<ShareCommandResult> UpdateAccessTierAsync(string itemId, string accessTier, bool allowDependencyConflicts, CancellationToken cancellationToken = default) =>
            Task.FromResult(Command);

        public Task<ShareCommandResult> PreviewDependenciesAsync(string itemId, string targetTier, CancellationToken cancellationToken = default) =>
            Task.FromResult(Command);

        public Task<ShareCommandResult> MintPublicLinkAsync(string itemId, DateTimeOffset? expiresAt, CancellationToken cancellationToken = default) =>
            Task.FromResult(Command);

        public Task<ShareCommandResult> RevokePublicLinkAsync(string itemId, string tokenId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Command);

        public Task<ShareCommandResult> SetEmbedAsync(string itemId, bool enabled, string? audience, bool allowDependencyConflicts, CancellationToken cancellationToken = default) =>
            Task.FromResult(Command);

        public Task<ShareCommandResult> MintEmbedTokenAsync(string itemId, string audience, int? ttlSeconds, CancellationToken cancellationToken = default) =>
            Task.FromResult(Command);
    }
}
