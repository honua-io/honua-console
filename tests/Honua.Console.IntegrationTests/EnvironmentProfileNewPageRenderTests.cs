using Bunit;
using Honua.Console.Native.Core.Connections;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free render coverage for the native environment-profile create page (<c>/environments/new</c>)
/// after the Wave-5 migration: the former ad-hoc CreateAsync checks now run through the shared
/// <c>EnvironmentProfileValidator</c> and render inline via <c>ValidationMessageInline</c>, the Create button
/// is gated on blocking client findings, and the page hosts an <c>&lt;UnsavedChangesGuard/&gt;</c> so editing
/// marks the form dirty (the context runs Loose JSInterop for the guard's JS module import / confirm()).
/// </summary>
public sealed class EnvironmentProfileNewPageRenderTests
{
    [Fact]
    public void DefaultForm_HttpsScheme_IsValid_AndCreateEnabled()
    {
        var page = Render();

        page.WaitForAssertion(
            () => Assert.NotNull(page.Find("input[placeholder='Honua Production']")),
            TimeSpan.FromSeconds(5));

        // Server URL defaults to "https://" (no host) and display name is blank, so the form gates initially
        // only after an edit triggers validation. Fill a valid form -> Create enabled.
        page.Find("input[placeholder='Honua Production']").Change("Honua Prod");
        page.Find("input[placeholder='https://prod.honua.example']").Change("https://prod.honua.example");

        page.WaitForAssertion(
            () => Assert.False(FindCreateButton(page).HasAttribute("disabled")),
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void NonHttpsServerUrl_ShowsInlineError_AndGatesCreate()
    {
        var page = Render();

        page.WaitForAssertion(
            () => Assert.NotNull(page.Find("input[placeholder='Honua Production']")),
            TimeSpan.FromSeconds(5));

        page.Find("input[placeholder='Honua Production']").Change("Honua Prod");
        page.Find("input[placeholder='https://prod.honua.example']").Change("http://insecure.example");

        page.WaitForAssertion(
            () => Assert.Contains("Server URL must be an absolute https URL", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.True(FindCreateButton(page).HasAttribute("disabled"));
        Assert.Contains("console-validation-inline", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void MtlsEnabledWithoutCert_ShowsCertRequiredError()
    {
        var page = Render();

        page.WaitForAssertion(
            () => Assert.NotNull(page.Find("#native-mtls")),
            TimeSpan.FromSeconds(5));

        page.Find("input[placeholder='Honua Production']").Change("Honua Prod");
        page.Find("input[placeholder='https://prod.honua.example']").Change("https://prod.honua.example");
        page.Find("#native-mtls").Change(true);

        page.WaitForAssertion(
            () => Assert.Contains("certificate reference value is required", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.True(FindCreateButton(page).HasAttribute("disabled"));

        // Supplying the cert clears the blocker.
        page.Find("input[placeholder='CN=Honua Operator']").Change("CN=Honua Operator");
        page.WaitForAssertion(
            () => Assert.False(FindCreateButton(page).HasAttribute("disabled")),
            TimeSpan.FromSeconds(5));
    }

    private static AngleSharp.Dom.IElement FindCreateButton(IRenderedComponent<EnvironmentProfileNewPage> page) =>
        page.FindAll("button").First(b => b.TextContent.Contains("Create environment", StringComparison.Ordinal));

    private static IRenderedComponent<EnvironmentProfileNewPage> Render()
    {
        var ctx = new Bunit.BunitContext();
        // The page hosts an <UnsavedChangesGuard/> (Wave 5); run Loose JSInterop so its JS module import /
        // confirm() calls no-op in render tests.
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IConsoleEnvironmentProfileStore>(new InMemoryConsoleEnvironmentProfileStore([]));
        ctx.Services.AddSingleton<IConsoleHostCapabilities>(new NativeConsoleHostCapabilities());
        return ctx.Render<EnvironmentProfileNewPage>();
    }
}
