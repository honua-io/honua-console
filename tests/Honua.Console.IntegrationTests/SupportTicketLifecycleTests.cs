using Bunit;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class SupportTicketLifecycleTests
{
    [Theory]
    [InlineData(OperateSectionStatus.Allowed)]
    [InlineData(OperateSectionStatus.Forbidden)]
    public void DelayedOldTicketCannotReplaceCurrentTicket(OperateSectionStatus oldStatus)
    {
        var client = new TicketClient();
        using var context = Context(client);
        var page = context.Render<SupportTicketDetailPage>(p => p.Add(c => c.TicketId, "old"));
        page.Render(p => p.Add(c => c.TicketId, "current"));
        page.WaitForAssertion(() => Assert.Contains("action-current", page.Markup));
        var renders = page.RenderCount;
        client.Pending.SetResult(oldStatus == OperateSectionStatus.Allowed
            ? Ticket("old") : SupportTicketResult.Denied(oldStatus, "Old ticket denied"));
        page.WaitForState(() => page.RenderCount > renders);
        Assert.Contains("action-current", page.Markup);
        Assert.DoesNotContain("action-old", page.Markup);
        Assert.DoesNotContain("Old ticket denied", page.Markup);
        Assert.True(client.PendingToken.IsCancellationRequested);
    }

    [Fact]
    public async Task DisposalCancelsPendingTicketRead()
    {
        var client = new TicketClient();
        using var context = Context(client);
        context.Render<SupportTicketDetailPage>(p => p.Add(c => c.TicketId, "old"));
        await context.DisposeComponentsAsync();
        Assert.True(client.PendingToken.IsCancellationRequested);
        client.Pending.SetResult(Ticket("old", "diagnosing"));
        await context.Renderer.Dispatcher.InvokeAsync(() => Task.CompletedTask);
        Assert.Single(client.Requests);
    }

    [Fact]
    public void EmptyRouteCancelsReadAndKeepsMissingStateAfterLateResponse()
    {
        var client = new TicketClient();
        using var context = Context(client);
        var page = context.Render<SupportTicketDetailPage>(p => p.Add(c => c.TicketId, "old"));
        page.Render(p => p.Add(c => c.TicketId, ""));
        var renders = page.RenderCount;
        client.Pending.SetResult(Ticket("old"));
        page.WaitForState(() => page.RenderCount > renders);
        Assert.Contains("No ticket id was supplied", page.Markup);
        Assert.DoesNotContain("action-old", page.Markup);
        Assert.True(client.PendingToken.IsCancellationRequested);
    }

    [Theory]
    [InlineData(OperateSectionStatus.Forbidden)]
    [InlineData(OperateSectionStatus.Missing)]
    [InlineData(OperateSectionStatus.Unavailable)]
    public void UnreadableRefreshRemovesPreviouslyLoadedDetails(OperateSectionStatus status)
    {
        var client = new TicketClient { RefreshStatus = status };
        using var context = Context(client);
        var page = context.Render<SupportTicketDetailPage>(p => p.Add(c => c.TicketId, "polling"));
        page.WaitForAssertion(() => Assert.Contains("action-polling", page.Markup));
        page.WaitForAssertion(() =>
        {
            Assert.Contains("Ticket no longer readable", page.Markup);
            Assert.DoesNotContain("action-polling", page.Markup);
            Assert.Empty(page.FindAll(".support-phase-badge"));
        }, TimeSpan.FromSeconds(10));
    }

    private static BunitContext Context(TicketClient client)
    {
        var context = new BunitContext();
        context.Services.AddSingleton<IConsoleSupportTicketClient>(client);
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = true });
        return context;
    }

    private static SupportTicketResult Ticket(string id, string phase = "resolved") =>
        SupportTicketResult.Allowed(new SupportTicketResponse
        {
            Id = id,
            CustomerId = "customer",
            Phase = phase,
            RequestedAction = "action-" + id
        });

    private sealed class TicketClient : IConsoleSupportTicketClient
    {
        public TaskCompletionSource<SupportTicketResult> Pending { get; } = new();
        public CancellationToken PendingToken { get; private set; }
        public List<string> Requests { get; } = [];
        public OperateSectionStatus RefreshStatus { get; init; }

        public Task<SupportTicketResult> GetTicketAsync(string ticketId, CancellationToken cancellationToken = default)
        {
            Requests.Add(ticketId);
            if (ticketId == "old")
            {
                PendingToken = cancellationToken;
                return Pending.Task;
            }
            return Task.FromResult(ticketId != "polling" ? Ticket(ticketId)
                : Requests.Count == 1 ? Ticket(ticketId, "diagnosing")
                : SupportTicketResult.Denied(RefreshStatus, "Ticket no longer readable"));
        }

        public Task<SupportTicketResult> CreateTicketAsync(CreateSupportTicketRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Ticket detail must never create a ticket.");
    }
}
