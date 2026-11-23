using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed partial class ApprovalInboxPageRenderTests
{
    private const string LinkedIntro = "[data-approval-request-intro]";
    private const string LinkedNext = "[data-approval-request-next]";

    private static BunitContext NewLinkedRequestContext(ConsolePresentationMode presentation)
    {
        var proposals = new FakeConsoleProposalsClient(
            proposals:
            [
                FakeProposalFactory.Summary("deploy-1", ConsoleProposalKind.Deploy) with
                {
                    RequestedBy = "kai@example.org",
                    RequestedByAgent = "Claude Desktop",
                },
                FakeProposalFactory.Summary("import-1", ConsoleProposalKind.DataImport),
            ],
            details:
            [
                FakeProposalFactory.Detail("deploy-1", ConsoleProposalKind.Deploy),
                FakeProposalFactory.Detail("import-1", ConsoleProposalKind.DataImport),
            ]);
        var ctx = NewContext(proposals, registerServices: context =>
            context.Services.AddSingleton(new ConsoleV4Options { Enabled = presentation == ConsolePresentationMode.V4 }));
        ctx.Services.GetRequiredService<NavigationManager>().NavigateTo("/inbox?proposalId=deploy-1");
        return ctx;
    }

    [Theory]
    [InlineData(ConsolePresentationMode.Legacy)]
    [InlineData(ConsolePresentationMode.V4)]
    public void LinkedRequest_NamesTheAgentAndExplainsWhatHappensNext(ConsolePresentationMode presentation)
    {
        using var ctx = NewLinkedRequestContext(presentation);
        var page = ctx.Render<ApprovalInboxPage>();

        page.WaitForAssertion(() => page.Find("[data-proposal-approve]"));
        if (presentation == ConsolePresentationMode.Legacy)
        {
            Assert.Empty(page.FindAll(LinkedIntro));
            return;
        }

        Assert.Equal("Claude Desktop is asking you to approve a change", page.Find($"{LinkedIntro} h2").TextContent);
        Assert.Contains("never counts as approval", page.Find(LinkedIntro).TextContent, StringComparison.Ordinal);
        Assert.Single(page.FindAll(LinkedNext));
        Assert.Single(page.FindAll(".approval-inbox-layout.is-linked-request"));
        CatalogAdminPreview.Capture("approvals-request", $"<div class=\"console-shell\" data-console-v4=\"true\">{page.Markup}</div>");
    }

    [Fact]
    public void LinkedRequest_ShowAllApprovalsKeepsTheReviewAndReturnsToTheQueue()
    {
        using var ctx = NewLinkedRequestContext(ConsolePresentationMode.V4);
        var page = ctx.Render<ApprovalInboxPage>();
        page.WaitForAssertion(() => page.Find("[data-show-all-approvals]"));

        page.Find("[data-show-all-approvals]").Click();

        Assert.Empty(page.FindAll(LinkedIntro));
        Assert.Empty(page.FindAll(".is-linked-request"));
        Assert.Single(page.FindAll("[data-proposal-approve]"));
    }

    [Fact]
    public void LinkedRequest_EndsWhenAnotherProposalIsSelected()
    {
        using var ctx = NewLinkedRequestContext(ConsolePresentationMode.V4);
        var page = ctx.Render<ApprovalInboxPage>();
        page.WaitForAssertion(() => page.Find(LinkedIntro));

        page.Find("[data-proposal-id='import-1']").Click();

        page.WaitForAssertion(() => Assert.Empty(page.FindAll(LinkedIntro)));
        Assert.Empty(page.FindAll(LinkedNext));
    }

    [Fact]
    public async Task LinkedRequest_StartsAgainWhenALinkOpensOnTheSamePage()
    {
        using var ctx = NewLinkedRequestContext(ConsolePresentationMode.V4);
        var page = ctx.Render<ApprovalInboxPage>();
        page.WaitForAssertion(() => page.Find(LinkedIntro));
        page.Find("[data-proposal-id='import-1']").Click();
        page.WaitForAssertion(() => Assert.Empty(page.FindAll(LinkedIntro)));

        var navigation = ctx.Services.GetRequiredService<NavigationManager>();
        await page.InvokeAsync(() => navigation.NavigateTo("/inbox?proposalId=deploy-1"));

        page.WaitForAssertion(() => Assert.Equal("true", page.Find(LinkedIntro).GetAttribute("data-request-waiting")));
    }

    [Fact]
    public void LinkedRequest_SaysWhenTheRequestIsNoLongerWaiting()
    {
        var proposals = new FakeConsoleProposalsClient(
            proposals: [FakeProposalFactory.Summary("done-1", ConsoleProposalKind.Deploy, ConsoleProposalStatus.Completed)],
            details: [FakeProposalFactory.Detail("done-1", ConsoleProposalKind.Deploy, ConsoleProposalStatus.Completed)]);
        using var ctx = NewContext(proposals, registerServices: context =>
            context.Services.AddSingleton(new ConsoleV4Options { Enabled = true }));
        ctx.Services.GetRequiredService<NavigationManager>().NavigateTo("/inbox?proposalId=done-1");

        var page = ctx.Render<ApprovalInboxPage>();

        page.WaitForAssertion(() => Assert.Equal("This request is no longer waiting for approval", page.Find($"{LinkedIntro} h2").TextContent));
        Assert.DoesNotContain("stays waiting", page.Find(LinkedNext).TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void LinkedRequest_FollowsTheLatestDetailWhenTheQueueIsStale()
    {
        var proposals = new FakeConsoleProposalsClient(
            proposals: [FakeProposalFactory.Summary("stale-1", ConsoleProposalKind.Deploy)],
            details: [FakeProposalFactory.Detail("stale-1", ConsoleProposalKind.Deploy, ConsoleProposalStatus.Rejected)]);
        using var ctx = NewContext(proposals, registerServices: context =>
            context.Services.AddSingleton(new ConsoleV4Options { Enabled = true }));
        ctx.Services.GetRequiredService<NavigationManager>().NavigateTo("/inbox?proposalId=stale-1");

        var page = ctx.Render<ApprovalInboxPage>();

        page.WaitForAssertion(() => Assert.Equal("false", page.Find(LinkedIntro).GetAttribute("data-request-waiting")));
        Assert.DoesNotContain("stays waiting", page.Find(LinkedNext).TextContent, StringComparison.Ordinal);
    }
}
