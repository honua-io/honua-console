using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class EnvironmentProfilesPageRenderTests
{
    private static ConsoleEnvironmentProfile Profile(string id, string name) =>
        new()
        {
            Id = id,
            DisplayName = name,
            ServerBaseUri = new Uri("https://demo.honua.io/"),
            EnvironmentKind = "development"
        };

    [Fact]
    public void ActiveBrowserProfile_ReportsSelectionWithoutClaimingConnection()
    {
        // Selection is visible even before a successful authenticated server read.
        var store = new InMemoryConsoleEnvironmentProfileStore(
            [Profile("dev", "Local honua-server")],
            states: null,
            activeProfileId: "dev");
        using var ctx = NewBrowserContext(store);

        var page = ctx.Render<EnvironmentProfilesPage>();

        page.WaitForAssertion(
            () => Assert.Contains("Selected in this browser session", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.DoesNotContain(">never<", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AccountBoundTenant_HidesIsolationMetadataWithoutChangingProfile()
    {
        var profile = Profile("test", "Test environment") with
        {
            TenantId = "profile-isolated",
            Account = new ConsoleAccountBinding { AccountId = "operator", TenantId = "account-isolated" }
        };
        var store = new InMemoryConsoleEnvironmentProfileStore([profile]);
        using var ctx = NewBrowserContext(store);

        var page = ctx.Render<EnvironmentProfilesPage>();

        page.WaitForAssertion(() => Assert.Equal("Test environment", page.Find("h2").TextContent));
        Assert.Empty(page.FindAll("[data-tenancy-preview]"));
        Assert.DoesNotContain("<dt>Tenant</dt>", page.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("profile-isolated", page.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("account-isolated", page.Markup, StringComparison.Ordinal);
        var saved = await store.GetProfileAsync("test");
        Assert.Equal("profile-isolated", saved?.TenantId);
        Assert.Equal("account-isolated", saved?.Account.TenantId);
    }

    [Fact]
    public void SingleTenantProfile_DoesNotShowTenancyPreviewNotice()
    {
        var store = new InMemoryConsoleEnvironmentProfileStore(
            [Profile("dev", "Local honua-server")],
            states: null,
            activeProfileId: "dev");
        using var ctx = NewBrowserContext(store);

        var page = ctx.Render<EnvironmentProfilesPage>();

        page.WaitForAssertion(
            () => Assert.Contains("Selected in this browser session", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Empty(page.FindAll("[data-tenancy-preview]"));
    }

    [Fact]
    public void ActiveProfileCard_StatesHowAccessIsGranted()
    {
        var store = new InMemoryConsoleEnvironmentProfileStore(
            [Profile("dev", "Local honua-server")],
            states: null,
            activeProfileId: "dev");
        using var ctx = NewBrowserContext(store);

        var page = ctx.Render<EnvironmentProfilesPage>();

        page.WaitForAssertion(
            () => Assert.NotEmpty(page.FindAll("[data-browser-alt-note]")),
            TimeSpan.FromSeconds(5));
        Assert.Contains("sign-in and permissions", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ActivatingProfile_RefreshesCapabilitiesForTheNewEnvironment()
    {
        var store = new InMemoryConsoleEnvironmentProfileStore(
            [Profile("dev", "Development"), Profile("prod", "Production")],
            states: null,
            activeProfileId: "dev");
        var manifest = new RecordingCapabilityManifest();
        using var ctx = NewBrowserContext(store, manifest);

        var page = ctx.Render<EnvironmentProfilesPage>();
        page.FindAll("button")
            .Single(button => button.TextContent.Contains("Use Environment", StringComparison.Ordinal)
                && !button.HasAttribute("disabled"))
            .Click();

        Assert.Equal(1, manifest.RefreshCount);
    }

    private static Bunit.BunitContext NewBrowserContext(
        IConsoleEnvironmentProfileStore store,
        IConsoleCapabilityManifest? manifest = null)
    {
        var ctx = new Bunit.BunitContext();
        ctx.Services.AddSingleton(store);
        ctx.Services.AddSingleton<IConsoleCapabilityManifest>(manifest ?? new ConsoleCapabilityManifest());
        return ctx;
    }

    private sealed class RecordingCapabilityManifest : IConsoleCapabilityManifest
    {
        public int RefreshCount { get; private set; }

        public Task RefreshAsync(CancellationToken cancellationToken = default)
        {
            RefreshCount++;
            return Task.CompletedTask;
        }

        public bool IsAdvertised(string capabilityKey) => false;
    }
}
