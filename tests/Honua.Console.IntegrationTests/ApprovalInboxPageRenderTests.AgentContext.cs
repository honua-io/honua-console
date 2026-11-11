using Bunit;
using Honua.Console.Shell.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Honua.Console.IntegrationTests;

public sealed partial class ApprovalInboxPageRenderTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AgentContext_TracksSelectionFiltersAndQueryNavigation(bool enabled)
    {
        var registry = new ConsoleAgentContextRegistry();
        var proposals = new FakeConsoleProposalsClient(
            proposals: [FakeProposalFactory.Summary("first", ConsoleProposalKind.Deploy),
                FakeProposalFactory.Summary("second", ConsoleProposalKind.Deploy)],
            details: [FakeProposalFactory.Detail("first", ConsoleProposalKind.Deploy),
                FakeProposalFactory.Detail("second", ConsoleProposalKind.Deploy)]);
        using var ctx = NewContext(proposals, registerServices: context =>
        {
            context.Services.AddSingleton(new ConsoleV4Options { Enabled = enabled });
            context.Services.AddSingleton(registry);
        });
        var navigation = ctx.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/inbox?proposalId=first");
        var page = ctx.Render<ApprovalInboxPage>();
        page.WaitForAssertion(() => page.Find("[data-proposal-approve]"));
        Assert.Equal(enabled ? "first" : null, registry.Current.Entities.SingleOrDefault()?.Id);
        Assert.Equal(enabled ? "/inbox" : "/", registry.Current.Route);
        page.Find("[data-filter='ServerUpgrade']").Click();
        if (enabled) Assert.Equal("ServerUpgrade", registry.Current.Filters["ticketType"]);
        page.Find("[data-proposal-id='second']").Click();
        page.Render();
        Assert.Equal(enabled ? "second" : null, registry.Current.Entities.SingleOrDefault()?.Id);

        if (enabled) Assert.Contains("proposalId=second", navigation.Uri, StringComparison.Ordinal);
        await page.InvokeAsync(() => navigation.NavigateTo("/inbox?proposalId=first"));
        if (enabled) page.WaitForAssertion(() => Assert.Equal("first", Assert.Single(registry.Current.Entities).Id));
        await page.InvokeAsync(() => navigation.NavigateTo("/inbox?proposalId=second"));
        page.WaitForAssertion(() => page.Find("[data-proposal-approve]"));
        page.Find("[data-refresh-inbox]").Click();
        Assert.Equal(0, registry.Current.Unsaved.PendingOperationCount);
        page.Find("[data-proposal-approve]").Click();
        page.WaitForAssertion(() => Assert.Equal("second", Assert.Single(proposals.Approved)));
        if (enabled)
        {
            Assert.Equal("second", Assert.Single(registry.Current.Selection).Id);
            Assert.Equal(ConsoleAgentEntityKind.Approval, Assert.Single(registry.Current.Entities).Kind);
        }
        await page.InvokeAsync(() => navigation.NavigateTo("/inbox"));
        page.WaitForAssertion(() => Assert.Empty(page.FindAll("[data-proposal-approve]")));
        Assert.Empty(registry.Current.Entities);
        await ctx.DisposeComponentsAsync();
        Assert.Empty(registry.Current.Filters);
        Assert.Empty(registry.Current.Selection);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AgentContext_ReportsReasonCountsWithoutLeakingTextOrStalePanelState(bool enabled)
    {
        var registry = new ConsoleAgentContextRegistry();
        var proposals = new FakeConsoleProposalsClient(
            proposals: [FakeProposalFactory.Summary("first", ConsoleProposalKind.Deploy),
                FakeProposalFactory.Summary("second", ConsoleProposalKind.Deploy)],
            details: [FakeProposalFactory.Detail("first", ConsoleProposalKind.Deploy),
                FakeProposalFactory.Detail("second", ConsoleProposalKind.Deploy)]);
        using var ctx = NewContext(proposals, registerServices: context =>
        {
            context.Services.AddSingleton(new ConsoleV4Options { Enabled = enabled });
            context.Services.AddSingleton(registry);
        });
        var page = ctx.Render<ApprovalInboxPage>();
        page.WaitForAssertion(() => page.Find("[data-proposal-id='first']"));
        page.Find("[data-proposal-id='first']").Click();
        page.WaitForAssertion(() => page.Find("[data-proposal-reason]"));
        page.Find("[data-proposal-reason]").Input("Private rejection explanation");
        page.WaitForAssertion(() => Assert.Equal(enabled ? 1 : 0, registry.Current.Unsaved.ChangedFieldCount));
        Assert.DoesNotContain("Private rejection explanation", System.Text.Json.JsonSerializer.Serialize(registry.Current));

        page.Find("[data-proposal-id='second']").Click();
        page.WaitForAssertion(() => Assert.Equal("", page.Find("[data-proposal-reason]").TextContent));
        Assert.Equal(0, registry.Current.Unsaved.ChangedFieldCount);
        page.Find("[data-proposal-reason]").Input("Second reason");
        page.Find(".approval-inbox-detail > button").Click();
        page.WaitForAssertion(() => Assert.Equal(0, registry.Current.Unsaved.ChangedFieldCount));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AgentContext_ReportsPendingDecisionUntilServerResponds(bool enabled)
    {
        var registry = new ConsoleAgentContextRegistry();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var proposals = new FakeConsoleProposalsClient(
            proposals: [FakeProposalFactory.Summary("first", ConsoleProposalKind.Deploy)],
            details: [FakeProposalFactory.Detail("first", ConsoleProposalKind.Deploy)])
        { ApprovalDelay = release.Task };
        using var ctx = NewContext(proposals, registerServices: context =>
        {
            context.Services.AddSingleton(new ConsoleV4Options { Enabled = enabled });
            context.Services.AddSingleton(registry);
        });
        var page = ctx.Render<ApprovalInboxPage>();
        page.WaitForAssertion(() => page.Find("[data-proposal-id='first']"));
        page.Find("[data-proposal-id='first']").Click();
        page.WaitForAssertion(() => page.Find("[data-proposal-approve]"));
        var decision = page.Find("[data-proposal-approve]").ClickAsync(new MouseEventArgs());
        try
        {
            page.WaitForAssertion(() => Assert.True(page.Find("[data-proposal-approve]").HasAttribute("disabled")));
            Assert.Equal(enabled ? 1 : 0, registry.Current.Unsaved.PendingOperationCount);
        }
        finally
        {
            release.SetResult();
            await decision;
        }
        page.WaitForAssertion(() => Assert.Equal(0, registry.Current.Unsaved.PendingOperationCount));
        Assert.Equal("first", Assert.Single(proposals.Approved));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AgentContext_KeepsPendingDecisionWhenSwitchingProposals(bool enabled)
    {
        var registry = new ConsoleAgentContextRegistry();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var proposals = new FakeConsoleProposalsClient(
            proposals: [FakeProposalFactory.Summary("first", ConsoleProposalKind.Deploy),
                FakeProposalFactory.Summary("second", ConsoleProposalKind.Deploy)],
            details: [FakeProposalFactory.Detail("first", ConsoleProposalKind.Deploy),
                FakeProposalFactory.Detail("second", ConsoleProposalKind.Deploy)])
        { ApprovalDelay = release.Task };
        using var ctx = NewContext(proposals, registerServices: context =>
        {
            context.Services.AddSingleton(new ConsoleV4Options { Enabled = enabled });
            context.Services.AddSingleton(registry);
        });
        var page = ctx.Render<ApprovalInboxPage>();
        page.WaitForAssertion(() => page.Find("[data-proposal-id='first']"));
        page.Find("[data-proposal-id='first']").Click();
        page.WaitForAssertion(() => page.Find("[data-proposal-approve]"));
        var decision = page.Find("[data-proposal-approve]").ClickAsync(new MouseEventArgs());
        try
        {
            page.WaitForAssertion(() => Assert.True(page.Find("[data-proposal-approve]").HasAttribute("disabled")));
            page.Find("[data-proposal-id='second']").Click();
            page.WaitForAssertion(() => Assert.False(page.Find("[data-proposal-approve]").HasAttribute("disabled")));
            page.Find("[data-proposal-reason]").Input("Second private reason");
            page.WaitForAssertion(() => Assert.Equal(enabled ? 1 : 0, registry.Current.Unsaved.PendingOperationCount));
            Assert.Equal(enabled ? 1 : 0, registry.Current.Unsaved.ChangedFieldCount);
        }
        finally
        {
            release.SetResult();
            await decision;
        }
        page.WaitForAssertion(() => Assert.Equal(0, registry.Current.Unsaved.PendingOperationCount));
        Assert.Equal(enabled ? 1 : 0, registry.Current.Unsaved.ChangedFieldCount);
        Assert.Equal("first", Assert.Single(proposals.Approved));
    }

}
