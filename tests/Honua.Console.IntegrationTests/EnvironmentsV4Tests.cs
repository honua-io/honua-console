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
    [InlineData(ConsolePresentationMode.Legacy)]
    [InlineData(ConsolePresentationMode.V4)]
    public void EnvironmentListKeepsProfilesTrustAndActions(ConsolePresentationMode presentation)
    {
        using var context = Context(presentation, Store());
        var page = context.Render<EnvironmentProfilesPage>();

        page.WaitForAssertion(() => Assert.Equal(["Development", "Production"], page.FindAll("article h2").Select(heading => heading.TextContent)));
        Assert.Equal("Environments", page.Find("h1").TextContent);
        Assert.Single(page.FindAll("a[href$='/environments/new']"));
        Assert.Equal(2, page.FindAll("a[href$='/environments/dev'], a[href$='/environments/prod']").Count);
        Assert.Contains(BlockedMessage, page.Find("[role='alert']").TextContent);
        Assert.Equal([true, false], page.FindAll("button").Select(button => button.HasAttribute("disabled")));
        AssertPresentation(page, presentation);
        if (presentation == ConsolePresentationMode.V4)
        {
            CatalogAdminPreview.CaptureV4("environments-v4", page.Markup);
        }
    }

    [Theory]
    [InlineData(ConsolePresentationMode.Legacy)]
    [InlineData(ConsolePresentationMode.V4)]
    public void EmptyEnvironmentListKeepsFirstRunPanel(ConsolePresentationMode presentation)
    {
        using var context = Context(presentation, new InMemoryConsoleEnvironmentProfileStore([]));
        var page = context.Render<EnvironmentProfilesPage>();

        page.WaitForAssertion(() => Assert.Equal("Add your first environment", page.Find("article h2").TextContent));
        Assert.Equal(2, page.FindAll("a[href$='/environments/new']").Count);
        Assert.Empty(page.FindAll("button"));
    }

    [Theory]
    [InlineData(ConsolePresentationMode.Legacy)]
    [InlineData(ConsolePresentationMode.V4)]
    public async Task UsingAnEnvironmentStillActivatesItAndRefreshesCapabilities(ConsolePresentationMode presentation)
    {
        var store = Store();
        var manifest = new RecordingCapabilityManifest();
        using var context = Context(presentation, store, manifest);
        var page = context.Render<EnvironmentProfilesPage>();

        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll("button").Count));
        page.FindAll("button").Single(button => !button.HasAttribute("disabled")).Click();

        Assert.Equal(1, manifest.RefreshCount);
        Assert.Equal("prod", (await store.GetActiveProfileAsync())?.Id);
    }

    [Theory]
    [InlineData(ConsolePresentationMode.Legacy)]
    [InlineData(ConsolePresentationMode.V4)]
    public void AddEnvironmentKeepsFieldsValidationAndCancel(ConsolePresentationMode presentation)
    {
        using var context = Context(presentation, new InMemoryConsoleEnvironmentProfileStore([]));
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var page = context.Render<EnvironmentProfileNewPage>();

        Assert.Equal("Add environment", page.Find("h1").TextContent);
        page.Find("#environment-display-name").Change("Honua Prod");
        page.Find("#environment-server-url").Change("http://insecure.example");

        page.WaitForAssertion(() => Assert.NotNull(page.Find("[data-environment-validation='blocking']")));
        Assert.True(page.FindAll("button").Single().HasAttribute("disabled"));
        Assert.Single(page.FindAll("a[href$='/environments']"));
        Assert.Equal(5, page.FindAll("input, select").Count);
        AssertPresentation(page, presentation);
        if (presentation == ConsolePresentationMode.V4)
        {
            CatalogAdminPreview.CaptureV4("environment-new-v4", page.Markup);
        }
    }

    [Theory]
    [InlineData(ConsolePresentationMode.Legacy)]
    [InlineData(ConsolePresentationMode.V4)]
    public void DiagnosticsKeepTrustTransportAndSessionEvidence(ConsolePresentationMode presentation)
    {
        using var context = Context(presentation, Store());
        var page = context.Render<EnvironmentProfileDetailPage>(parameters => parameters.Add(p => p.ProfileId, "prod"));

        page.WaitForAssertion(() => Assert.Equal("Production", page.Find("h1").TextContent));
        Assert.Contains("https://prod.honua.example/", page.Find("h1").ParentElement!.TextContent);
        Assert.Contains(BlockedMessage, page.Find("[role='alert']").TextContent);
        Assert.Equal(["Environment access blocked", "Transports", "Trust state", "Session"], page.FindAll("h2").Select(heading => heading.TextContent));
        Assert.Contains("/settings/prod", page.Markup);
        Assert.Equal("Trust blocked", page.Find(presentation == ConsolePresentationMode.V4 ? ".console-page-heading-actions .console-status" : ".console-detail-actions .console-status").TextContent);
        Assert.Single(page.FindAll("a[href$='/environments']"));
        AssertPresentation(page, presentation);
        if (presentation == ConsolePresentationMode.V4)
        {
            CatalogAdminPreview.CaptureV4("environment-detail-v4", page.Markup);
        }
    }

    [Theory]
    [InlineData(ConsolePresentationMode.Legacy)]
    [InlineData(ConsolePresentationMode.V4)]
    public void UnknownEnvironmentKeepsNotFoundState(ConsolePresentationMode presentation)
    {
        using var context = Context(presentation, Store());
        var page = context.Render<EnvironmentProfileDetailPage>(parameters => parameters.Add(p => p.ProfileId, "missing"));

        page.WaitForAssertion(() => Assert.Equal("Environment not found", page.Find("h1").TextContent));
        Assert.Equal("missing", page.Find("code").TextContent);
        Assert.Empty(page.FindAll(".console-page-heading-v4"));
    }

    private static void AssertPresentation<TPage>(IRenderedComponent<TPage> page, ConsolePresentationMode presentation)
        where TPage : Microsoft.AspNetCore.Components.IComponent
    {
        var enabled = presentation == ConsolePresentationMode.V4;
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

    private static BunitContext Context(ConsolePresentationMode presentation, IConsoleEnvironmentProfileStore store, IConsoleCapabilityManifest? manifest = null)
    {
        var context = new BunitContext();
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = presentation == ConsolePresentationMode.V4 });
        context.Services.AddSingleton(store);
        context.Services.AddSingleton(manifest ?? new RecordingCapabilityManifest());
        return context;
    }
}
