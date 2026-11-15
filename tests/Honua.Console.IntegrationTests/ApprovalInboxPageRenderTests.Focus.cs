using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed partial class ApprovalInboxPageRenderTests
{
    private const string FocusIdentifier = "Blazor._internal.domWrapper.focus";

    [Theory]
    [InlineData(ConsolePresentationMode.Legacy)]
    [InlineData(ConsolePresentationMode.V4)]
    public void Selection_MovesFocusToTheReviewAndCloseReturnsItToTheRow(ConsolePresentationMode presentation)
    {
        using var ctx = NewFocusContext(presentation);
        var page = ctx.Render<ApprovalInboxPage>();
        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll("[data-proposal-id]").Count));
        Assert.Null(page.Find("[data-approval-review]").GetAttribute("aria-live"));

        page.Find("[data-proposal-id='deploy-1']").Click();
        page.WaitForAssertion(() => page.Find("[data-proposal-approve]"));

        var reviewFocus = Assert.Single(FocusCalls(ctx));
        var reviewTarget = presentation == ConsolePresentationMode.V4
            ? page.Find("[data-approval-review-heading]")
            : page.FindAll("[data-approval-review] button").Single(button => button.TextContent == "Close review");
        reviewFocus.Arguments[0].ShouldBeElementReferenceTo(reviewTarget);

        page.FindAll("[data-approval-review] button").Single(button => button.TextContent == "Close review").Click();

        page.WaitForAssertion(() => Assert.Equal(2, FocusCalls(ctx).Count));
        FocusCalls(ctx)[1].Arguments[0].ShouldBeElementReferenceTo(page.Find("[data-proposal-id='deploy-1']"));
        Assert.Empty(page.FindAll("[data-proposal-approve]"));
    }

    [Fact]
    public void DeepLinkSelection_DoesNotMoveFocus()
    {
        using var ctx = NewFocusContext(ConsolePresentationMode.V4);
        ctx.Services.GetRequiredService<NavigationManager>().NavigateTo("/inbox?proposalId=deploy-1");

        var page = ctx.Render<ApprovalInboxPage>();

        page.WaitForAssertion(() => page.Find("[data-proposal-approve]"));
        Assert.Empty(FocusCalls(ctx));
    }

    [Theory]
    [InlineData(ConsolePresentationMode.Legacy)]
    [InlineData(ConsolePresentationMode.V4)]
    public void CloseReview_AfterTheRowMoves_FocusesThatProposalsRow(ConsolePresentationMode presentation)
    {
        using var ctx = NewFocusContext(presentation);
        var page = ctx.Render<ApprovalInboxPage>();
        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll("[data-proposal-id]").Count));
        var secondId = page.FindAll("[data-proposal-id]")[1].GetAttribute("data-proposal-id");
        page.Find($"[data-proposal-id='{secondId}']").Click();
        page.WaitForAssertion(() => Assert.Single(FocusCalls(ctx)));

        page.Find(secondId == "import-1" ? "[data-filter='DataImport']" : "[data-filter='ServerUpgrade']").Click();
        Assert.Equal(secondId, Assert.Single(page.FindAll("[data-proposal-id]")).GetAttribute("data-proposal-id"));
        page.FindAll("[data-approval-review] button").Single(button => button.TextContent == "Close review").Click();

        page.WaitForAssertion(() => Assert.Equal(2, FocusCalls(ctx).Count));
        FocusCalls(ctx)[1].Arguments[0].ShouldBeElementReferenceTo(page.Find($"[data-proposal-id='{secondId}']"));
    }

    [Fact]
    public void CloseReview_AfterTheRowIsFilteredOut_FocusesTheReviewHeading()
    {
        using var ctx = NewFocusContext(ConsolePresentationMode.V4);
        var page = ctx.Render<ApprovalInboxPage>();
        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll("[data-proposal-id]").Count));
        page.Find("[data-proposal-id='deploy-1']").Click();
        page.WaitForAssertion(() => page.Find("[data-proposal-approve]"));

        page.Find("[data-filter='DataImport']").Click();
        Assert.Empty(page.FindAll("[data-proposal-id='deploy-1']"));
        page.FindAll("[data-approval-review] button").Single(button => button.TextContent == "Close review").Click();

        page.WaitForAssertion(() => Assert.Equal(2, FocusCalls(ctx).Count));
        FocusCalls(ctx)[1].Arguments[0].ShouldBeElementReferenceTo(page.Find("[data-approval-review-heading]"));
        Assert.Empty(page.FindAll("[data-proposal-approve]"));
    }

    private static BunitContext NewFocusContext(ConsolePresentationMode presentation)
    {
        var proposals = new FakeConsoleProposalsClient(
            proposals:
            [
                FakeProposalFactory.Summary("deploy-1", ConsoleProposalKind.Deploy, summary: "Upgrade the server"),
                FakeProposalFactory.Summary("import-1", ConsoleProposalKind.DataImport, summary: "Import parcels"),
            ],
            details:
            [
                FakeProposalFactory.Detail("deploy-1", ConsoleProposalKind.Deploy),
                FakeProposalFactory.Detail("import-1", ConsoleProposalKind.DataImport),
            ]);
        return NewContext(proposals, registerServices: context =>
            context.Services.AddSingleton(new ConsoleV4Options { Enabled = presentation == ConsolePresentationMode.V4 }));
    }

    private static List<JSRuntimeInvocation> FocusCalls(BunitContext ctx) =>
        ctx.JSInterop.Invocations.Where(call => call.Identifier == FocusIdentifier).ToList();
}
