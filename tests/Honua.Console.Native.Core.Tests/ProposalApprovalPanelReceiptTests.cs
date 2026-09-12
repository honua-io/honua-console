using Bunit;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.Native.Core.Tests;

public sealed class ProposalApprovalPanelReceiptTests
{
    [Fact]
    public void DeepLinkedResolvedProposalRemainsInspectableOutsidePendingQueue()
    {
        using var context = new BunitContext();
        context.Services.AddSingleton<IConsoleProposalsClient>(new ProposalClient(ConsoleProposalStatus.Submitted));
        context.Services.AddSingleton<IConsoleApprovalInboxClient>(new EmptyInboxClient());
        context.Services.AddSingleton<IConsoleProposalRealtimeClient>(new NoOpRealtimeClient());

        var navigation = context.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo(navigation.GetUriWithQueryParameter("proposalId", "proposal-1"));
        var page = context.Render<ApprovalInboxPage>();

        page.WaitForAssertion(() =>
        {
            Assert.Equal("Submitted", page.Find("[data-proposal-status]").GetAttribute("data-proposal-status"));
            Assert.Equal("operation-1", page.Find("[data-correlation-kind=\"OperationId\"]")
                .GetAttribute("data-correlation-id"));
        });
    }

    [Fact]
    public void ApprovalKeepsServerStatusAndExecutionOperationMachineReadable()
    {
        using var context = new BunitContext();
        context.Services.AddSingleton<IConsoleProposalsClient>(new ProposalClient());
        var panel = context.Render<ProposalApprovalPanel>(parameters => parameters
            .Add(component => component.ProposalId, "proposal-1"));

        panel.WaitForAssertion(() =>
            Assert.Equal("AwaitingApproval", panel.Find("[data-proposal-status]").GetAttribute("data-proposal-status")));

        panel.Find("[data-proposal-approve]").Click();

        panel.WaitForAssertion(() =>
        {
            Assert.Equal("Submitted", panel.Find("[data-proposal-status]").GetAttribute("data-proposal-status"));
            var operation = panel.Find("[data-correlation-kind=\"OperationId\"]");
            Assert.Equal("operation-1", operation.GetAttribute("data-correlation-id"));
            Assert.Contains("Approved", panel.Find("[data-proposal-action-message]").TextContent, StringComparison.Ordinal);
        });
    }

    private sealed class ProposalClient(ConsoleProposalStatus initialStatus = ConsoleProposalStatus.AwaitingApproval)
        : IConsoleProposalsClient
    {
        public Task<OperateSectionResult<IReadOnlyList<ConsoleProposalSummary>>> ListAsync(
            string? status = null,
            string? kind = null,
            string? requestedBy = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<IReadOnlyList<ConsoleProposalSummary>>.Allowed([]));

        public Task<OperateSectionResult<ConsoleProposalDetail>> GetAsync(
            string proposalId,
            CancellationToken cancellationToken = default) => Task.FromResult(Allowed(initialStatus));

        public Task<OperateSectionResult<ConsoleProposalDetail>> ApproveAsync(
            string proposalId,
            CancellationToken cancellationToken = default) => Task.FromResult(Allowed(ConsoleProposalStatus.Submitted));

        public Task<OperateSectionResult<ConsoleProposalDetail>> RejectAsync(
            string proposalId,
            string reason,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<ConsoleProposalDetail>.Denied(OperateSectionStatus.Unsupported, "not used"));

        private static OperateSectionResult<ConsoleProposalDetail> Allowed(ConsoleProposalStatus status) =>
            OperateSectionResult<ConsoleProposalDetail>.Allowed(new(
                "proposal-1",
                ConsoleProposalKind.MetadataRelease,
                status,
                "agent",
                "agent",
                "Publish candidate map",
                ["item-1", "version-1", "draft-1", "map-route"],
                [],
                ConsoleProposalRisk.Low,
                [],
                [],
                "human",
                status == ConsoleProposalStatus.AwaitingApproval ? null : "operator",
                null,
                status == ConsoleProposalStatus.AwaitingApproval ? null : "operation-1",
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                status == ConsoleProposalStatus.AwaitingApproval ? null : DateTimeOffset.UtcNow));
    }

    private sealed class EmptyInboxClient : IConsoleApprovalInboxClient
    {
        public Task<OperateSectionResult<ApprovalInboxSnapshot>> GetInboxAsync(
            string? status = null,
            string? kind = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<ApprovalInboxSnapshot>.Allowed(ApprovalInboxSnapshot.Empty));
    }

    private sealed class NoOpRealtimeClient : IConsoleProposalRealtimeClient
    {
        public event Action<ConsoleProposalEvent>? ProposalChanged { add { } remove { } }
        public bool IsConnected => false;
        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
