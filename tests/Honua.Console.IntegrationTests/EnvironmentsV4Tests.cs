using Bunit;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class EnvironmentsV4Tests
{
    private const string BlockedMessage = "The server certificate changed since it was pinned.";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EnvironmentListKeepsProfilesTrustAndActions(bool enabled)
    {
        using var context = Context(enabled, Store());
        var page = context.Render<EnvironmentProfilesPage>();

        page.WaitForAssertion(() => Assert.Equal(["Development", "Production"], page.FindAll("article h2").Select(heading => heading.TextContent)));
        Assert.Equal("Environments", page.Find("h1").TextContent);
        Assert.Single(page.FindAll("a[href$='/environments/new']"));
        Assert.Equal(2, page.FindAll("a[href$='/environments/dev'], a[href$='/environments/prod']").Count);
        Assert.Contains(BlockedMessage, page.Find("[role='alert']").TextContent);
        Assert.Equal([true, false], page.FindAll("button").Select(button => button.HasAttribute("disabled")));
        AssertPresentation(page, enabled);
        if (enabled)
        {
            CaptureV4("environments-v4", page.Markup);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmptyEnvironmentListKeepsFirstRunPanel(bool enabled)
    {
        using var context = Context(enabled, new InMemoryConsoleEnvironmentProfileStore([]));
        var page = context.Render<EnvironmentProfilesPage>();

        page.WaitForAssertion(() => Assert.Equal("Add your first environment", page.Find("article h2").TextContent));
        Assert.Equal(2, page.FindAll("a[href$='/environments/new']").Count);
        Assert.Empty(page.FindAll("button"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UsingAnEnvironmentStillActivatesItAndRefreshesCapabilities(bool enabled)
    {
        var store = Store();
        var manifest = new RecordingCapabilityManifest();
        using var context = Context(enabled, store, manifest);
        var page = context.Render<EnvironmentProfilesPage>();

        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll("button").Count));
        page.FindAll("button").Single(button => !button.HasAttribute("disabled")).Click();

        Assert.Equal(1, manifest.RefreshCount);
        Assert.Equal("prod", (await store.GetActiveProfileAsync())?.Id);
    }

    private static void AssertPresentation<TPage>(IRenderedComponent<TPage> page, bool enabled)
        where TPage : Microsoft.AspNetCore.Components.IComponent
    {
        Assert.Equal(enabled ? 1 : 0, page.FindAll(".console-page-heading-v4").Count);
        Assert.Equal(enabled ? 0 : 1, page.FindAll(".console-page-heading:not(.console-page-heading-v4)").Count);
        Assert.Equal(enabled ? 1 : 0, page.FindAll(".console-environments-v4 > link[rel='stylesheet'][href$='css/environments-v4.css']").Count);
    }

    private static InMemoryConsoleEnvironmentProfileStore Store() => new(
        [Profile("dev", "Development", "development"), Profile("prod", "Production", "production")],
        [new ConsoleEnvironmentState
        {
            ProfileId = "prod",
            LastRoute = "/settings/prod",
            TrustBlocked = true,
            Trust = new HonuaEnvironmentTrustState { Status = HonuaCertificateValidationStatus.Missing, SanitizedMessage = BlockedMessage },
        }],
        activeProfileId: "dev");

    private static ConsoleEnvironmentProfile Profile(string id, string name, string kind) => new()
    {
        Id = id,
        DisplayName = name,
        ServerBaseUri = new Uri($"https://{id}.honua.example/"),
        EnvironmentKind = kind,
    };

    private static BunitContext Context(bool enabled, IConsoleEnvironmentProfileStore store, IConsoleCapabilityManifest? manifest = null)
    {
        var context = new BunitContext();
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = enabled });
        context.Services.AddSingleton(store);
        context.Services.AddSingleton(manifest ?? new RecordingCapabilityManifest());
        return context;
    }

    private static void CaptureV4(string name, string markup) =>
        CatalogAdminPreview.Capture(name, $"<div class=\"console-shell\" data-console-v4=\"true\">{markup}</div>");
}
