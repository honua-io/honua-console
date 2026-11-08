using Bunit;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class EnvironmentProfileNewPageRenderTests
{
    [Fact]
    public void EnvironmentSetup_HidesTenantSelection()
    {
        var page = Render();

        Assert.Empty(page.FindAll("[data-tenancy-preview], input[placeholder='trial-a']"));
        Assert.DoesNotContain("Tenant ID", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Account settings", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void DefaultForm_HttpsScheme_IsValid_AndConnectEnabled()
    {
        var page = Render();

        page.WaitForAssertion(
            () => Assert.NotNull(page.Find("#environment-display-name")),
            TimeSpan.FromSeconds(5));

        // Server URL defaults to "https://" (no host) and display name is blank, so the form gates initially
        // only after an edit triggers validation. Fill a valid form -> Connect enabled.
        page.Find("#environment-display-name").Change("Honua Prod");
        page.Find("#environment-server-url").Change("https://prod.honua.example");

        page.WaitForAssertion(
            () => Assert.False(FindConnectButton(page).HasAttribute("disabled")),
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void NonHttpsServerUrl_ShowsInlineError_AndGatesConnect()
    {
        var page = Render();

        page.WaitForAssertion(
            () => Assert.NotNull(page.Find("#environment-display-name")),
            TimeSpan.FromSeconds(5));

        page.Find("#environment-display-name").Change("Honua Prod");
        page.Find("#environment-server-url").Change("http://insecure.example");

        page.WaitForAssertion(
            () => Assert.Contains("Server URL must be an absolute https URL", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.True(FindConnectButton(page).HasAttribute("disabled"));
        Assert.Contains("console-validation-inline", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidConnect_CreatesActivatesProfile_AndRoutesToSignIn()
    {
        var store = new InMemoryConsoleEnvironmentProfileStore([]);
        var ctx = CreateContext(store);
        // A successful create marks the form clean before redirecting; the guard's confirm() should not block
        // the redirect. Allow it to proceed defensively so the assertion targets routing, not guard timing.
        ctx.JSInterop.Setup<bool>("confirm", _ => true).SetResult(true);
        var page = ctx.Render<EnvironmentProfileNewPage>();
        var nav = ctx.Services.GetRequiredService<Bunit.TestDoubles.BunitNavigationManager>();

        page.WaitForAssertion(
            () => Assert.NotNull(page.Find("#environment-display-name")),
            TimeSpan.FromSeconds(5));

        page.Find("#environment-display-name").Change("Acme Server");
        page.Find("#environment-server-url").Change("https://gis.acme.example");

        var connect = FindConnectButton(page);
        page.WaitForAssertion(() => Assert.False(connect.HasAttribute("disabled")), TimeSpan.FromSeconds(5));
        connect.Click();

        // A created profile has no client certificate, is activated, and the user is sent
        // straight to sign-in so the first run is not a dead-end.
        page.WaitForAssertion(
            () => Assert.EndsWith("/auth/signin", nav.Uri, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        var active = await store.GetActiveProfileAsync();
        Assert.NotNull(active);
        Assert.Equal("https://gis.acme.example/", active!.ServerBaseUri.ToString());
        Assert.False(active.ClientCertificate.Enabled);
    }

    private static AngleSharp.Dom.IElement FindConnectButton(IRenderedComponent<EnvironmentProfileNewPage> page) =>
        page.FindAll("button").First(b => b.TextContent.Contains("Connect and continue", StringComparison.Ordinal));

    private static IRenderedComponent<EnvironmentProfileNewPage> Render() =>
        CreateContext(new InMemoryConsoleEnvironmentProfileStore([])).Render<EnvironmentProfileNewPage>();

    private static Bunit.BunitContext CreateContext(IConsoleEnvironmentProfileStore store)
    {
        var ctx = new Bunit.BunitContext();
        // The page hosts an <UnsavedChangesGuard/> (Wave 5); run Loose JSInterop so its JS module import /
        // confirm() calls no-op in render tests.
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IConsoleEnvironmentProfileStore>(store);
        return ctx;
    }
}
