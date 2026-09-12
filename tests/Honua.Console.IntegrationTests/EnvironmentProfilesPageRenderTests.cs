using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free render coverage for the Environment Profiles card affordances hardened in the UCD sweep
/// (honua-console#313): the active browser profile reports live use instead of "Last connected: never", and
/// the browser host states what a browser user can do rather than leaving a bare native-only "Connect" dead end.
/// </summary>
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
    public void ActiveBrowserProfile_ReportsLiveUse_NotLastConnectedNever()
    {
        // The active profile the browser Console binds and serves automatically is live now; "never"
        // misrepresented it (honua-console#313 item 6, "Last connected reflects actual live use").
        var store = new InMemoryConsoleEnvironmentProfileStore(
            [Profile("dev", "Local honua-server")],
            states: null,
            activeProfileId: "dev");
        using var ctx = NewBrowserContext(store);

        var page = ctx.Render<EnvironmentProfilesPage>();

        page.WaitForAssertion(
            () => Assert.Contains("In use now", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.DoesNotContain(">never<", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void BrowserProfileCard_StatesBrowserAlternative_ForNativeOnlyConnect()
    {
        // The native-only Connect must state the browser-user alternative, not dead-end (honua-console#313 item 3).
        var store = new InMemoryConsoleEnvironmentProfileStore(
            [Profile("dev", "Local honua-server")],
            states: null,
            activeProfileId: "dev");
        using var ctx = NewBrowserContext(store);

        var page = ctx.Render<EnvironmentProfilesPage>();

        page.WaitForAssertion(
            () => Assert.NotEmpty(page.FindAll("[data-browser-alt-note]")),
            TimeSpan.FromSeconds(5));
        // Native connect/validation is labelled desktop-only, and the active browser profile explains it is
        // already used automatically over HTTPS — an explanation of what the browser user CAN do.
        Assert.NotEmpty(page.FindAll("[data-native-only-note]"));
        Assert.Contains("desktop Console only", page.Markup, StringComparison.Ordinal);
        Assert.Contains("automatically", page.Markup, StringComparison.Ordinal);
        // The bare, guidance-free "Connect · Native host only" dead end is gone.
        Assert.DoesNotContain("Connect · Native host only", page.Markup, StringComparison.Ordinal);
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
        ctx.Services.AddSingleton<IConsoleHostCapabilities>(new BrowserConsoleHostCapabilities());
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
