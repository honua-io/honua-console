using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class OperateReleaseWitnessPageRenderTests
{
    private const string CompleteQuery = "/operate/release-witness?family=map&itemId=item-one&versionId=version-one&contentHash=hash-one&proposalId=proposal-one";
    private static readonly ConsoleReleaseWitnessEvidence Evidence = new(
        "server-revision-one", "map", "item-one", "version-one", "hash-one", "proposal-one",
        "publication-one", "https://example.test/maps/publication-one", "correlation-one", "operation-one", true);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompleteQueryPreservesWitnessAttributesAndPublicLink(bool enabled)
    {
        using var context = CreateContext(enabled);
        var client = new WitnessClient(Task.FromResult(OperateSectionResult<ConsoleReleaseWitnessEvidence>.Allowed(Evidence)));
        context.Services.AddSingleton<IConsoleReleaseWitnessClient>(client);
        context.Services.GetRequiredService<NavigationManager>().NavigateTo(CompleteQuery);

        var page = context.Render<OperateReleaseWitnessPage>();

        Assert.Equal(new ConsoleReleaseWitnessRequest("map", "item-one", "version-one", "hash-one", "proposal-one"), Assert.Single(client.Requests));
        AssertPresentation(page, enabled);
        var witness = page.Find("[data-release-witness]");
        Assert.Equal("server-revision-one", witness.GetAttribute("data-server-source-revision"));
        Assert.Equal("map", witness.GetAttribute("data-family"));
        Assert.Equal("item-one", witness.GetAttribute("data-item-id"));
        Assert.Equal("version-one", witness.GetAttribute("data-version-id"));
        Assert.Equal("hash-one", witness.GetAttribute("data-content-hash"));
        Assert.Equal("proposal-one", witness.GetAttribute("data-proposal-id"));
        Assert.Equal("publication-one", witness.GetAttribute("data-publication-id"));
        Assert.Equal(Evidence.PublicUrl, witness.GetAttribute("data-public-url"));
        Assert.Equal("correlation-one", witness.GetAttribute("data-audit-correlation-id"));
        Assert.Equal("operation-one", witness.GetAttribute("data-audit-operation-id"));
        Assert.Equal("true", witness.GetAttribute("data-audit-verified"));
        Assert.Equal(Evidence.PublicUrl, page.Find("[data-release-public-link]").GetAttribute("href"));
        if (enabled) Capture("release-witness-verified", page.Markup);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IncompleteQueryDoesNotReadOrInventEvidence(bool enabled)
    {
        using var context = CreateContext(enabled);
        var client = new WitnessClient(Task.FromResult(OperateSectionResult<ConsoleReleaseWitnessEvidence>.Allowed(Evidence)));
        context.Services.AddSingleton<IConsoleReleaseWitnessClient>(client);
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("/operate/release-witness?family=map");

        var page = context.Render<OperateReleaseWitnessPage>();

        AssertPresentation(page, enabled);
        Assert.Empty(client.Requests);
        Assert.Empty(page.FindAll("[data-release-witness]"));
        Assert.Contains("requires a complete sealed candidate query", page.Markup, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeniedEvidencePreservesServerMessageAndDiagnostics(bool enabled)
    {
        using var context = CreateContext(enabled);
        context.Services.AddSingleton<IConsoleReleaseWitnessClient>(new WitnessClient(Task.FromResult(
            OperateSectionResult<ConsoleReleaseWitnessEvidence>.Denied(OperateSectionStatus.Forbidden, "Evidence access is denied.", "request-denied-one"))));
        context.Services.GetRequiredService<NavigationManager>().NavigateTo(CompleteQuery);

        var page = context.Render<OperateReleaseWitnessPage>();

        AssertPresentation(page, enabled);
        Assert.Empty(page.FindAll("[data-release-witness], [data-release-public-link]"));
        Assert.Contains("Evidence access is denied.", page.Markup, StringComparison.Ordinal);
        Assert.Contains("request-denied-one", page.Markup, StringComparison.Ordinal);
        if (enabled) Capture("release-witness-denied", page.Markup);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LoadingDoesNotExposeVerifiedEvidenceBeforeResponse(bool enabled)
    {
        using var context = CreateContext(enabled);
        var response = new TaskCompletionSource<OperateSectionResult<ConsoleReleaseWitnessEvidence>>();
        context.Services.AddSingleton<IConsoleReleaseWitnessClient>(new WitnessClient(response.Task));
        context.Services.GetRequiredService<NavigationManager>().NavigateTo(CompleteQuery);

        var page = context.Render<OperateReleaseWitnessPage>();

        AssertPresentation(page, enabled);
        Assert.Contains("Loading...", page.Markup, StringComparison.Ordinal);
        Assert.Empty(page.FindAll("[data-release-witness]"));
        await page.InvokeAsync(() => response.SetResult(OperateSectionResult<ConsoleReleaseWitnessEvidence>.Allowed(Evidence)));
        page.WaitForAssertion(() => Assert.Single(page.FindAll("[data-release-witness]")));
        AssertPresentation(page, enabled);
    }

    private static BunitContext CreateContext(bool enabled)
    {
        var context = new BunitContext();
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = enabled });
        return context;
    }

    private static void Capture(string name, string markup) =>
        CatalogAdminPreview.Capture(name, $"<div class=\"console-shell\" data-console-v4=\"true\">{markup}</div>");

    private static void AssertPresentation(IRenderedComponent<OperateReleaseWitnessPage> page, bool enabled)
    {
        Assert.Equal(enabled ? "Publication evidence" : "Candidate publication evidence", Assert.Single(page.FindAll("h1")).TextContent);
        Assert.Equal(enabled, page.FindAll(".console-release-witness-v4").Count == 1);
        Assert.Equal(enabled && page.FindAll("[data-release-witness]").Count == 0,
            page.FindAll("h2.console-visually-hidden[data-release-witness-state-heading]").Count == 1);
        Assert.Equal(enabled, page.FindAll(".console-page-heading-v4").Count == 1);
        Assert.Equal(enabled, page.FindAll("section > link[rel='stylesheet'][href$='release-witness-v4.css']").Count == 1);
    }

    private sealed class WitnessClient(Task<OperateSectionResult<ConsoleReleaseWitnessEvidence>> result) : IConsoleReleaseWitnessClient
    {
        public List<ConsoleReleaseWitnessRequest> Requests { get; } = [];

        public Task<OperateSectionResult<ConsoleReleaseWitnessEvidence>> ObserveAsync(ConsoleReleaseWitnessRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return result;
        }
    }
}
