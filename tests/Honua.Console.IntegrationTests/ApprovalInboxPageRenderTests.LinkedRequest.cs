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
            proposals: [FakeProposalFactory.Summary("deploy-1", ConsoleProposalKind.Deploy),
                FakeProposalFactory.Summary("import-1", ConsoleProposalKind.DataImport)],
            details:
            [
                FakeProposalFactory.Detail("deploy-1", ConsoleProposalKind.Deploy) with
                {
                    RequestedBy = "kai@example.org",
                    RequestedByAgent = "Claude Desktop",
                },
                FakeProposalFactory.Detail("import-1", ConsoleProposalKind.DataImport),
            ]);
        return NewLinkedRequestContext(proposals, "deploy-1", presentation);
    }

    private static BunitContext NewLinkedRequestContext(
        FakeConsoleProposalsClient proposals, string proposalId, ConsolePresentationMode presentation = ConsolePresentationMode.V4)
    {
        var ctx = NewContext(proposals, registerServices: context =>
            context.Services.AddSingleton(new ConsoleV4Options { Enabled = presentation == ConsolePresentationMode.V4 }));
        ctx.Services.GetRequiredService<NavigationManager>().NavigateTo($"/inbox?proposalId={proposalId}");
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
        Assert.Contains("checks approval permissions", page.Find(LinkedIntro).TextContent, StringComparison.Ordinal);
        var layout = page.Find(".approval-inbox-layout.is-linked-request");
        Assert.True(layout.Children[0].HasAttribute("data-approval-review"));
        Assert.Single(page.FindAll(LinkedNext));
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
    public async Task LinkedRequest_StartsAgainWhenALinkOpensOnTheSamePage()
    {
        using var ctx = NewLinkedRequestContext(ConsolePresentationMode.V4);
        var page = ctx.Render<ApprovalInboxPage>();
        page.WaitForAssertion(() => page.Find(LinkedIntro));
        page.Find("[data-proposal-id='import-1']").Click();
        page.WaitForAssertion(() => Assert.Empty(page.FindAll(LinkedIntro)));

        var navigation = ctx.Services.GetRequiredService<NavigationManager>();
        Assert.Contains("source=serving", navigation.Uri, StringComparison.Ordinal);
        await page.InvokeAsync(() => navigation.NavigateTo("/inbox?proposalId=deploy-1"));
        page.WaitForAssertion(() => Assert.Equal("Claude Desktop is asking you to approve a change", page.Find($"{LinkedIntro} h2").TextContent));
    }

    [Fact]
    public void LinkedRequest_ShowsTheOutcomeAfterTheProposalLeavesTheQueue()
    {
        var proposals = new FakeConsoleProposalsClient(
            proposals: [],
            details: [FakeProposalFactory.Detail("done-1", ConsoleProposalKind.Deploy, ConsoleProposalStatus.Completed)]);
        using var ctx = NewLinkedRequestContext(proposals, "done-1");
        var page = ctx.Render<ApprovalInboxPage>();

        page.WaitForAssertion(() => Assert.Equal("This request is no longer waiting for approval", page.Find($"{LinkedIntro} h2").TextContent));
        Assert.DoesNotContain("stays waiting", page.Find(LinkedNext).TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void LinkedRequest_DoesNotUseTheQueueWhenDetailIsMissing()
    {
        var proposals = new FakeConsoleProposalsClient(
            proposals: [FakeProposalFactory.Summary("missing-1", ConsoleProposalKind.Deploy)]);
        using var ctx = NewLinkedRequestContext(proposals, "missing-1");
        var page = ctx.Render<ApprovalInboxPage>();

        page.WaitForAssertion(() => Assert.Contains("Proposal not found.", page.Markup, StringComparison.Ordinal));
        Assert.Empty(page.FindAll(LinkedIntro));
        Assert.Empty(page.FindAll(LinkedNext));
    }
}
