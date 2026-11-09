using Bunit;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Honua.Console.IntegrationTests;

public sealed class ConsoleSupportV4Tests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RolloutPreservesTicketFormAndContext(bool enabled)
    {
        using var context = Context(enabled);
        var page = context.Render<SupportPage>();
        page.WaitForAssertion(() => Assert.Contains("Console version", page.Markup));
        Assert.Equal(enabled ? "Support" : "Contact support", page.Find("h1").TextContent);
        Assert.Equal(enabled ? 1 : 0, page.FindAll(".console-page-heading-v4").Count);
        Assert.Equal(2, page.FindAll("select").Count);
        Assert.Equal(4, page.FindAll("input:not([type=checkbox])").Count);
        Assert.Single(page.FindAll("textarea"));
        Assert.True(page.Find("button").HasAttribute("disabled"));
        Assert.Contains("Auto-attached context", page.Markup);
        Capture(page.Markup, enabled, "form");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SubmitRetainsAccessScopeSecretContextAndNavigation(bool enabled)
    {
        using var context = Context(enabled);
        var page = context.Render<SupportPage>();
        await page.InvokeAsync(() =>
        {
            page.FindAll("select")[0].Change("sev2");
            page.FindAll("select")[1].Change("guided-fix");
            page.FindAll("input:not([type=checkbox])")[0].Change(" custom-service ");
            page.FindAll("input:not([type=checkbox])")[1].Change(" diagnose errors ");
            page.FindAll("input:not([type=checkbox])")[2].Change("1");
            page.FindAll("input:not([type=checkbox])")[3].Change(" scoped-secret ");
            page.Find("textarea").Change(" elevated 500s ");
            page.Find("input[type=checkbox]").Change(true);
            page.Find("button").Click();
        });
        var client = context.Services.GetRequiredService<TicketClient>();
        page.WaitForAssertion(() => Assert.Single(client.Requests));
        var request = client.Requests.Single();
        Assert.Equal("sev2", request.Severity);
        Assert.Equal("custom-service", request.Service);
        Assert.Equal("diagnose errors", request.RequestedAction);
        Assert.Equal("elevated 500s", request.Symptoms);
        Assert.Equal("guided-fix", request.AllowedAccessMode);
        Assert.Equal(5, request.TtlMinutes);
        Assert.True(request.RollbackExpected);
        Assert.Equal("scoped-secret", request.Context!.ScopedKey);
        Assert.Equal("/support", request.Context.Route);
        Assert.Equal("production", request.Environment);
        Assert.EndsWith("/support/tickets/ticket-9", context.Services.GetRequiredService<NavigationManager>().Uri);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedSubmissionRetainsInputAndAllowsRetry(bool enabled)
    {
        using var context = Context(enabled);
        context.Services.GetRequiredService<TicketClient>().Result = SupportTicketResult.Denied(OperateSectionStatus.Forbidden, "Ticket creation is not allowed.");
        var page = context.Render<SupportPage>();
        page.FindAll("input:not([type=checkbox])")[1].Change("diagnose");
        page.Find("textarea").Change("errors");
        page.Find("button").Click();
        page.WaitForAssertion(() => Assert.Contains("Ticket creation is not allowed.", page.Find("[role=alert]").TextContent));
        Assert.False(page.Find("button").HasAttribute("disabled"));
        Assert.Equal("errors", page.Find("textarea").GetAttribute("value") ?? page.Find("textarea").TextContent);
        Assert.EndsWith("/support", context.Services.GetRequiredService<NavigationManager>().Uri);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TicketDetailPreservesStatusAccessAndRequestedAction(bool enabled)
    {
        using var context = Context(enabled);
        var page = context.Render<SupportTicketDetailPage>(parameters => parameters.Add(component => component.TicketId, "ticket-9"));
        page.WaitForAssertion(() => Assert.Contains("Status", page.Markup));
        Assert.Equal("Ticket ticket-9", page.Find("h1").TextContent);
        Assert.Equal(enabled ? 1 : 0, page.FindAll(".console-page-heading-v4").Count);
        Assert.Contains("resolved", page.Markup);
        Assert.Contains("read-only", page.Markup);
        Assert.Contains("diagnose errors", page.Markup);
        Assert.Contains("honua-server", page.Markup);
        Capture(page.Markup, enabled, "ticket");
    }

    [Theory]
    [InlineData(false, OperateSectionStatus.Forbidden)]
    [InlineData(true, OperateSectionStatus.Forbidden)]
    [InlineData(false, OperateSectionStatus.Missing)]
    [InlineData(true, OperateSectionStatus.Missing)]
    [InlineData(false, OperateSectionStatus.Unavailable)]
    [InlineData(true, OperateSectionStatus.Unavailable)]
    public void TicketFailureKeepsServerEvidenceAndReturnLink(bool enabled, OperateSectionStatus status)
    {
        using var context = Context(enabled);
        context.Services.GetRequiredService<TicketClient>().Result = SupportTicketResult.Denied(status, "Ticket cannot be read.");
        var page = context.Render<SupportTicketDetailPage>(parameters => parameters.Add(component => component.TicketId, "ticket-9"));
        page.WaitForAssertion(() => Assert.Contains("Ticket cannot be read.", page.Markup));
        Assert.Empty(page.FindAll(".support-phase-badge"));
        Assert.Equal("/support", page.Find("a").GetAttribute("href"));
    }

    private static void Capture(string markup, bool enabled, string kind) =>
        CatalogAdminPreview.Capture($"support-{kind}-{(enabled ? "v4" : "legacy")}", $"<div class=\"console-shell\" data-console-v4=\"{enabled.ToString().ToLowerInvariant()}\">{markup}</div>");

    private static BunitContext Context(bool enabled)
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = enabled });
        context.Services.AddSingleton(new TicketClient());
        context.Services.AddSingleton<IConsoleSupportTicketClient>(provider => provider.GetRequiredService<TicketClient>());
        var profiles = new InMemoryConsoleEnvironmentProfileStore([]);
        var sessions = new InMemoryConsoleAccountSessionStore();
        context.Services.AddSingleton(new HttpClient(new NoNetworkHandler()));
        context.Services.AddSingleton(provider => new ConsoleSupportContextBuilder(profiles, sessions,
            new HttpConsoleOperateObservabilityClient(provider.GetRequiredService<HttpClient>(), profiles, sessions)));
        context.Services.AddSingleton<IConsoleSupportAssistantClient, UnsupportedConsoleSupportAssistantClient>();
        context.Services.AddSingleton<IConsoleSupportKnowledgeBase, EmptyKnowledgeBase>();
        context.Services.AddSingleton<IConsoleSupportDeflectionLog>(new ConsoleSupportDeflectionLog(NullLogger<ConsoleSupportDeflectionLog>.Instance));
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("/support");
        return context;
    }

    private sealed class NoNetworkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("A support form without a connected environment must not make server requests.");
    }

    private sealed class EmptyKnowledgeBase : IConsoleSupportKnowledgeBase
    {
        public bool IsLoaded => false;
        public int RecordCount => 0;
        public IReadOnlyList<SupportKbMatch> Search(string query, int limit = 3) => [];
    }

    private sealed class TicketClient : IConsoleSupportTicketClient
    {
        public List<CreateSupportTicketRequest> Requests { get; } = [];
        public SupportTicketResult Result { get; set; } = SupportTicketResult.Allowed(new SupportTicketResponse
        {
            Id = "ticket-9",
            CustomerId = "honua-prod",
            Severity = "sev2",
            Environment = "production",
            Service = "honua-server",
            Symptoms = "elevated 500s",
            RequestedAction = "diagnose errors",
            AllowedAccessMode = "read-only",
            Phase = "resolved",
            CustomerStatus = "resolved",
            CreatedAt = DateTimeOffset.Parse("2026-06-05T12:00:00Z"),
            UpdatedAt = DateTimeOffset.Parse("2026-06-05T12:30:00Z")
        });
        public Task<SupportTicketResult> CreateTicketAsync(CreateSupportTicketRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(Result);
        }
        public Task<SupportTicketResult> GetTicketAsync(string ticketId, CancellationToken cancellationToken = default) => Task.FromResult(Result);
    }
}
