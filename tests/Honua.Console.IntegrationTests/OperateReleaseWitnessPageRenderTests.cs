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
    public async Task CompleteQueryPreservesWitnessAttributesAndPublicLink(bool enabled)
    {
        using var context = CreateContext(new ConsoleV4Options { Enabled = enabled });
        var registry = new ConsoleAgentContextRegistry();
        context.Services.AddSingleton(registry);
        var client = new WitnessClient(Task.FromResult(OperateSectionResult<ConsoleReleaseWitnessEvidence>.Allowed(Evidence)));
        context.Services.AddSingleton<IConsoleReleaseWitnessClient>(client);
        context.Services.GetRequiredService<NavigationManager>().NavigateTo(CompleteQuery);

        var page = context.Render<OperateReleaseWitnessPage>();

        Assert.Equal(new ConsoleReleaseWitnessRequest("map", "item-one", "version-one", "hash-one", "proposal-one"), Assert.Single(client.Requests));
        AssertPresentation(page, enabled ? "Publication evidence" : "Candidate publication evidence", enabled ? 1 : 0);
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
        Assert.Equal(enabled ? "/operate/release-witness" : "/", registry.Current.Route);
        Assert.Empty(registry.Current.Filters);
        Assert.Empty(registry.Current.Selection);
        Assert.Equal(enabled ? new[] { "item-one", "proposal-one" } : [], registry.Current.Entities.Select(entity => entity.Id));
        await context.DisposeComponentsAsync();
        Assert.Empty(registry.Current.Entities);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IncompleteQueryDoesNotReadOrInventEvidence(bool enabled)
    {
        using var context = CreateContext(new ConsoleV4Options { Enabled = enabled });
        var client = new WitnessClient(Task.FromResult(OperateSectionResult<ConsoleReleaseWitnessEvidence>.Allowed(Evidence)));
        context.Services.AddSingleton<IConsoleReleaseWitnessClient>(client);
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("/operate/release-witness?family=map");

        var page = context.Render<OperateReleaseWitnessPage>();

        AssertPresentation(page, enabled ? "Publication evidence" : "Candidate publication evidence", enabled ? 1 : 0);
        Assert.Empty(client.Requests);
        Assert.Empty(page.FindAll("[data-release-witness]"));
        Assert.Contains("requires a complete sealed candidate query", page.Markup, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeniedEvidencePreservesServerMessageAndDiagnostics(bool enabled)
    {
        using var context = CreateContext(new ConsoleV4Options { Enabled = enabled });
        context.Services.AddSingleton<IConsoleReleaseWitnessClient>(new WitnessClient(Task.FromResult(
            OperateSectionResult<ConsoleReleaseWitnessEvidence>.Denied(OperateSectionStatus.Forbidden, "Evidence access is denied.", "request-denied-one"))));
        context.Services.GetRequiredService<NavigationManager>().NavigateTo(CompleteQuery);

        var page = context.Render<OperateReleaseWitnessPage>();

        AssertPresentation(page, enabled ? "Publication evidence" : "Candidate publication evidence", enabled ? 1 : 0);
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
        using var context = CreateContext(new ConsoleV4Options { Enabled = enabled });
        var response = new TaskCompletionSource<OperateSectionResult<ConsoleReleaseWitnessEvidence>>();
        context.Services.AddSingleton<IConsoleReleaseWitnessClient>(new WitnessClient(response.Task));
        context.Services.GetRequiredService<NavigationManager>().NavigateTo(CompleteQuery);

        var page = context.Render<OperateReleaseWitnessPage>();

        AssertPresentation(page, enabled ? "Publication evidence" : "Candidate publication evidence", enabled ? 1 : 0);
        Assert.Contains("Loading...", page.Markup, StringComparison.Ordinal);
        Assert.Empty(page.FindAll("[data-release-witness]"));
        await page.InvokeAsync(() => response.SetResult(OperateSectionResult<ConsoleReleaseWitnessEvidence>.Allowed(Evidence)));
        page.WaitForAssertion(() => Assert.Single(page.FindAll("[data-release-witness]")));
        AssertPresentation(page, enabled ? "Publication evidence" : "Candidate publication evidence", enabled ? 1 : 0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NavigationClearsPriorWitnessAndIgnoresOutOfOrderResponses(bool checkPendingIdentity)
    {
        using var context = CreateContext(new ConsoleV4Options { Enabled = true });
        var registry = new ConsoleAgentContextRegistry();
        var client = new QueuedWitnessClient();
        context.Services.AddSingleton(registry);
        context.Services.AddSingleton<IConsoleReleaseWitnessClient>(client);
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo(CompleteQuery);
        var page = context.Render<OperateReleaseWitnessPage>();
        await page.InvokeAsync(() => client.Responses[0].SetResult(OperateSectionResult<ConsoleReleaseWitnessEvidence>.Allowed(Evidence)));
        page.WaitForAssertion(() => Assert.Equal(2, registry.Current.Entities.Count));
        navigation.NavigateTo(CompleteQuery.Replace("one", "two", StringComparison.Ordinal));
        page.WaitForAssertion(() => Assert.Equal(2, client.Responses.Count));
        if (checkPendingIdentity) Assert.Empty(registry.Current.Entities);
        navigation.NavigateTo(CompleteQuery.Replace("one", "three", StringComparison.Ordinal));
        page.WaitForAssertion(() => Assert.Equal(3, client.Responses.Count));
        var current = Evidence with { ItemId = "item-three", ProposalId = "proposal-three" };
        await page.InvokeAsync(() => client.Responses[2].SetResult(OperateSectionResult<ConsoleReleaseWitnessEvidence>.Allowed(current)));
        page.WaitForAssertion(() => Assert.Equal("item-three", registry.Current.Entities[0].Id));
        var renderCount = page.RenderCount;
        await page.InvokeAsync(() => client.Responses[1].SetResult(OperateSectionResult<ConsoleReleaseWitnessEvidence>.Allowed(Evidence)));
        page.WaitForState(() => page.RenderCount > renderCount);
        Assert.Equal("item-three", registry.Current.Entities[0].Id);
        Assert.Equal("item-three", page.Find("[data-release-witness]").GetAttribute("data-item-id"));
        await context.DisposeComponentsAsync();
    }

    private sealed class QueuedWitnessClient : IConsoleReleaseWitnessClient
    {
        public List<TaskCompletionSource<OperateSectionResult<ConsoleReleaseWitnessEvidence>>> Responses { get; } = [];
        public Task<OperateSectionResult<ConsoleReleaseWitnessEvidence>> ObserveAsync(ConsoleReleaseWitnessRequest request, CancellationToken cancellationToken = default)
        {
            var response = new TaskCompletionSource<OperateSectionResult<ConsoleReleaseWitnessEvidence>>(TaskCreationOptions.RunContinuationsAsynchronously);
            Responses.Add(response);
            return response.Task;
        }
    }

    private static BunitContext CreateContext(ConsoleV4Options options)
    {
        var context = new BunitContext();
        context.Services.AddSingleton(options);
        return context;
    }

    private static void Capture(string name, string markup) =>
        CatalogAdminPreview.Capture(name, $"<div class=\"console-shell\" data-console-v4=\"true\">{markup}</div>");

    private static void AssertPresentation(IRenderedComponent<OperateReleaseWitnessPage> page, string heading, int expectedV4Elements)
    {
        Assert.Equal(heading, Assert.Single(page.FindAll("h1")).TextContent);
        Assert.Equal(expectedV4Elements, page.FindAll(".console-release-witness-v4").Count);
        Assert.Equal(page.FindAll("[data-release-witness]").Count == 0 ? expectedV4Elements : 0,
            page.FindAll("h2.console-visually-hidden[data-release-witness-state-heading]").Count);
        Assert.Equal(expectedV4Elements, page.FindAll(".console-page-heading-v4").Count);
        Assert.Equal(expectedV4Elements, page.FindAll("section > link[rel='stylesheet'][href$='release-witness-v4.css']").Count);
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
