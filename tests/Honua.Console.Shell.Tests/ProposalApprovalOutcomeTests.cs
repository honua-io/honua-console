using Bunit;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;
using Honua.Console.TestFixtures;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.Shell.Tests;

public sealed class ProposalApprovalOutcomeTests
{
    private const string CatalogConflict = "The catalog changed after this folder proposal was submitted. Refresh the catalog and submit a fresh proposal.";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedApprovalShowsReasonAndNotifiesInboxWithoutClaimingSubmission(bool enabled)
    {
        using var context = CreateContext(new ConsoleV4Options { Enabled = enabled });
        context.Services.AddSingleton<IConsoleProposalsClient>(new ReceiptProposalClient
        {
            ApprovalStatus = ConsoleProposalStatus.Failed,
            ResolutionReason = CatalogConflict,
            BlockingReasons = [CatalogConflict],
        });
        var actions = new List<ConsoleProposalDetail>();
        var reports = new List<ConsoleAgentUnsavedSummary>();
        context.SetRendererInfo(new RendererInfo("Server", isInteractive: true));
        var panel = context.Render<ProposalApprovalPanel>(parameters => parameters
            .Add(component => component.ProposalId, "proposal-1")
            .Add(component => component.OnActioned, detail => actions.Add(detail))
            .Add(component => component.UnsavedChanged, summary => reports.Add(summary)));
        panel.Find("[data-proposal-reason]").Input("Reviewed catalog change");

        await panel.Find("[data-proposal-approve]").ClickAsync(new());

        var message = panel.Find("[data-proposal-action-message]");
        Assert.Contains("Approval recorded", message.TextContent, StringComparison.Ordinal);
        Assert.Contains("change was not applied", message.TextContent, StringComparison.Ordinal);
        Assert.Contains(CatalogConflict, message.TextContent, StringComparison.Ordinal);
        Assert.Contains("console-state-danger", message.ClassList);
        Assert.DoesNotContain("submitted for execution", message.TextContent, StringComparison.Ordinal);
        Assert.Equal("Failed", panel.Find("[data-proposal-status]").GetAttribute("data-proposal-status"));
        Assert.Equal("operation-1", panel.Find("[data-correlation-kind=\"OperationId\"]").GetAttribute("data-correlation-id"));
        Assert.Equal(ConsoleProposalStatus.Failed, Assert.Single(actions).Status);
        Assert.Equal(CatalogConflict, actions[0].ResolutionReason);
        Assert.Equal(new ConsoleAgentUnsavedSummary(), reports[^1]);
        Assert.Empty(panel.FindAll("[data-proposal-approve]"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task FailedApprovalUsesBlockingReasonsWhenResolutionReasonIsAbsent(string? reason)
    {
        using var context = CreateContext(new ConsoleV4Options { Enabled = true });
        context.Services.AddSingleton<IConsoleProposalsClient>(new ReceiptProposalClient
        {
            ApprovalStatus = ConsoleProposalStatus.Failed,
            ResolutionReason = reason,
            BlockingReasons = ["", CatalogConflict, "Requester must refresh."],
        });
        var panel = RenderPanel(context);

        await panel.Find("[data-proposal-approve]").ClickAsync(new());

        var message = panel.Find("[data-proposal-action-message]").TextContent;
        Assert.Contains(CatalogConflict, message, StringComparison.Ordinal);
        Assert.Contains("Requester must refresh.", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FailedApprovalWithoutReasonStillReportsFailure()
    {
        using var context = CreateContext(new ConsoleV4Options { Enabled = true });
        context.Services.AddSingleton<IConsoleProposalsClient>(new ReceiptProposalClient
        {
            ApprovalStatus = ConsoleProposalStatus.Failed,
        });
        var panel = RenderPanel(context);

        await panel.Find("[data-proposal-approve]").ClickAsync(new());

        Assert.Contains("change was not applied", panel.Find("[data-proposal-action-message]").TextContent, StringComparison.Ordinal);
        Assert.Contains("console-state-danger", panel.Find("[data-proposal-action-message]").ClassList);
    }

    [Theory]
    [InlineData(ConsoleProposalStatus.Submitted, "submitted for execution", "console-state-warning")]
    [InlineData(ConsoleProposalStatus.Executing, "execution is in progress", "console-state-warning")]
    [InlineData(ConsoleProposalStatus.Succeeded, "change applied", "console-state-success")]
    [InlineData(ConsoleProposalStatus.Completed, "execution completed", "console-state-success")]
    [InlineData(ConsoleProposalStatus.Cancelled, "execution was cancelled", "console-state-warning")]
    [InlineData(ConsoleProposalStatus.Rejected, "proposal was rejected", "console-state-warning")]
    [InlineData(ConsoleProposalStatus.RolledBack, "change was rolled back", "console-state-danger")]
    [InlineData(ConsoleProposalStatus.Indeterminate, "execution outcome is indeterminate", "console-state-warning")]
    [InlineData(ConsoleProposalStatus.Reconciling, "execution is being reconciled", "console-state-warning")]
    [InlineData(ConsoleProposalStatus.Unknown, "execution status is unknown", "console-state-neutral")]
    [InlineData(ConsoleProposalStatus.Planned, "execution is planned", "console-state-warning")]
    [InlineData(ConsoleProposalStatus.AwaitingApproval, "still awaiting approval", "console-state-warning")]
    public async Task ApprovalFeedbackUsesActualExecutionState(ConsoleProposalStatus status, string expected, string style)
    {
        using var context = CreateContext(new ConsoleV4Options { Enabled = true });
        context.Services.AddSingleton<IConsoleProposalsClient>(new ReceiptProposalClient { ApprovalStatus = status });
        var panel = RenderPanel(context);

        await panel.Find("[data-proposal-approve]").ClickAsync(new());

        var message = panel.Find("[data-proposal-action-message]");
        Assert.Contains(expected, message.TextContent, StringComparison.Ordinal);
        Assert.Contains(style, message.ClassList);
        Assert.Equal(status.ToString(), panel.Find("[data-proposal-status]").GetAttribute("data-proposal-status"));
    }

    [Theory]
    [InlineData(ConsoleProposalStatus.Failed)]
    [InlineData(ConsoleProposalStatus.Succeeded)]
    public async Task ResolutionReasonTakesPrecedenceOverBlockingReasons(ConsoleProposalStatus status)
    {
        using var context = CreateContext(new ConsoleV4Options { Enabled = true });
        context.Services.AddSingleton<IConsoleProposalsClient>(new ReceiptProposalClient
        {
            ApprovalStatus = status,
            ResolutionReason = "Current resolution from the server.",
            BlockingReasons = ["Earlier planning blocker."],
        });
        var panel = RenderPanel(context);

        await panel.Find("[data-proposal-approve]").ClickAsync(new());

        var message = panel.Find("[data-proposal-action-message]").TextContent;
        Assert.Contains("Current resolution from the server.", message, StringComparison.Ordinal);
        Assert.DoesNotContain("Earlier planning blocker.", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FailureReasonIsRenderedAsText()
    {
        using var context = CreateContext(new ConsoleV4Options { Enabled = true });
        const string reason = "<script>unexpected()</script>";
        context.Services.AddSingleton<IConsoleProposalsClient>(new ReceiptProposalClient
        {
            ApprovalStatus = ConsoleProposalStatus.Failed,
            ResolutionReason = reason,
        });
        var panel = RenderPanel(context);

        await panel.Find("[data-proposal-approve]").ClickAsync(new());

        var message = panel.Find("[data-proposal-action-message]");
        Assert.Contains(reason, message.TextContent, StringComparison.Ordinal);
        Assert.Empty(message.QuerySelectorAll("script"));
    }

    private static BunitContext CreateContext(ConsoleV4Options options)
    {
        var context = new BunitContext();
        context.Services.AddSingleton(options);
        return context;
    }

    private static IRenderedComponent<ProposalApprovalPanel> RenderPanel(BunitContext context)
    {
        context.SetRendererInfo(new RendererInfo("Server", isInteractive: true));
        return context.Render<ProposalApprovalPanel>(parameters => parameters.Add(component => component.ProposalId, "proposal-1"));
    }
}
