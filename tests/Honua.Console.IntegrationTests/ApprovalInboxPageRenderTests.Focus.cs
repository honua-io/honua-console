using System.Collections.Concurrent;
using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
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
        using var ctx = NewFocusContext(presentation, out var inbox);
        var page = ctx.Render<ApprovalInboxPage>();
        var references = TrackReferences(page, Heading, Row("deploy-1"));
        inbox.Open();
        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll(Rows).Count));
        Assert.Null(page.Find("[data-approval-review]").GetAttribute("aria-live"));

        ClickCurrent(page, Row("deploy-1"));
        page.WaitForAssertion(() => page.Find("[data-proposal-approve]"));

        var reviewFocus = Assert.Single(FocusCalls(ctx));
        if (presentation == ConsolePresentationMode.V4)
        {
            AssertFocused(reviewFocus, references, Heading);
        }
        else
        {
            // Close review is inserted and re-rendered in one batch, so bUnit never shows its id.
            Assert.DoesNotContain(Assert.IsType<ElementReference>(reviewFocus.Arguments[0]).Id, references.Values);
        }

        ClickCurrent(page, CloseReview);

        page.WaitForAssertion(() => Assert.Equal(2, FocusCalls(ctx).Count));
        AssertFocused(FocusCalls(ctx)[1], references, Row("deploy-1"));
        Assert.Empty(page.FindAll("[data-proposal-approve]"));
    }

    [Fact]
    public void DeepLinkSelection_DoesNotMoveFocus()
    {
        using var ctx = NewFocusContext(ConsolePresentationMode.V4, out var inbox);
        ctx.Services.GetRequiredService<NavigationManager>().NavigateTo("/inbox?proposalId=deploy-1");

        var page = ctx.Render<ApprovalInboxPage>();
        inbox.Open();

        page.WaitForAssertion(() => page.Find("[data-proposal-approve]"));
        Assert.Empty(FocusCalls(ctx));
    }

    [Theory]
    [InlineData(ConsolePresentationMode.Legacy)]
    [InlineData(ConsolePresentationMode.V4)]
    public void CloseReview_AfterTheRowMoves_FocusesTheReferenceCapturedForThatRow(ConsolePresentationMode presentation)
    {
        using var ctx = NewFocusContext(presentation, out var inbox);
        var page = ctx.Render<ApprovalInboxPage>();
        var references = TrackReferences(page, Row("deploy-1"), Row("import-1"));
        inbox.Open();
        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll(Rows).Count));
        var secondId = page.FindAll(Rows)[1].GetAttribute("data-proposal-id")!;
        ClickCurrent(page, Row(secondId));
        page.WaitForAssertion(() => Assert.Single(FocusCalls(ctx)));

        ClickCurrent(page, secondId == "import-1" ? "[data-filter='DataImport']" : "[data-filter='ServerUpgrade']");
        Assert.Equal(secondId, Assert.Single(page.FindAll(Rows)).GetAttribute("data-proposal-id"));
        ClickCurrent(page, CloseReview);

        page.WaitForAssertion(() => Assert.Equal(2, FocusCalls(ctx).Count));
        AssertFocused(FocusCalls(ctx)[1], references, Row(secondId));
    }

    [Theory]
    [InlineData(ConsolePresentationMode.Legacy)]
    [InlineData(ConsolePresentationMode.V4)]
    public void CloseReview_AfterTheRowIsFilteredOut_FocusesAPersistentHeading(ConsolePresentationMode presentation)
    {
        using var ctx = NewFocusContext(presentation, out var inbox);
        var page = ctx.Render<ApprovalInboxPage>();
        var fallbackHeading = presentation == ConsolePresentationMode.V4 ? Heading : InboxHeading;
        var references = TrackReferences(page, fallbackHeading);
        inbox.Open();
        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll(Rows).Count));
        ClickCurrent(page, Row("deploy-1"));
        page.WaitForAssertion(() => page.Find("[data-proposal-approve]"));

        ClickCurrent(page, "[data-filter='DataImport']");
        Assert.Empty(page.FindAll(Row("deploy-1")));
        ClickCurrent(page, CloseReview);

        page.WaitForAssertion(() => Assert.Equal(2, FocusCalls(ctx).Count));
        AssertFocused(FocusCalls(ctx)[1], references, fallbackHeading);
        Assert.Empty(page.FindAll("[data-proposal-approve]"));
    }

    [Theory]
    [InlineData(ConsolePresentationMode.Legacy)]
    [InlineData(ConsolePresentationMode.V4)]
    public void DuplicateProposalIds_RenderAndCloseReviewFallsBackToTheHeading(ConsolePresentationMode presentation)
    {
        var server = FakeProposalFactory.Summary("dup-1", ConsoleProposalKind.Deploy, summary: "Upgrade the server");
        var devOps = server with { Source = ConsoleProposalSource.DevOps };
        var inbox = new GatedApprovalInboxClient(new ScriptedApprovalInboxClient
        {
            Result = OperateSectionResult<ApprovalInboxSnapshot>.Allowed(FakeProposalFactory.Snapshot(server, devOps)),
        });
        using var ctx = NewContextWithInbox(inbox, registerServices: context =>
        {
            context.Services.AddSingleton(new ConsoleV4Options { Enabled = presentation == ConsolePresentationMode.V4 });
            context.Services.AddSingleton<IConsoleProposalsClient>(new FakeConsoleProposalsClient(
                proposals: [server], details: [FakeProposalFactory.Detail("dup-1", ConsoleProposalKind.Deploy)]));
        });
        var page = ctx.Render<ApprovalInboxPage>();
        var fallbackHeading = presentation == ConsolePresentationMode.V4 ? Heading : InboxHeading;
        var references = TrackReferences(page, fallbackHeading);
        inbox.Open();
        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll(Row("dup-1")).Count));

        ClickCurrent(page, Row("dup-1"));
        page.WaitForAssertion(() => Assert.Single(FocusCalls(ctx)));
        ClickCurrent(page, CloseReview);

        page.WaitForAssertion(() => Assert.Equal(2, FocusCalls(ctx).Count));
        AssertFocused(FocusCalls(ctx)[1], references, fallbackHeading);
    }

    private static BunitContext NewFocusContext(ConsolePresentationMode presentation, out GatedApprovalInboxClient inbox)
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
        inbox = new GatedApprovalInboxClient(new ConsoleApprovalInboxClient(proposals));
        return NewContextWithInbox(inbox, registerServices: context =>
        {
            context.Services.AddSingleton(new ConsoleV4Options { Enabled = presentation == ConsolePresentationMode.V4 });
            context.Services.AddSingleton<IConsoleProposalsClient>(proposals);
        });
    }

    private const string Rows = "button.approval-inbox-select[data-proposal-id]";
    private const string Heading = "[data-approval-review-heading]";
    private const string InboxHeading = "[data-approval-inbox-heading]";
    private const string CloseReview = "[data-approval-review] > button.console-button-secondary";

    private static string Row(string proposalId) => $"button.approval-inbox-select[data-proposal-id='{proposalId}']";

    // bUnit writes an element's reference id only into the render that inserts the element, so
    // record each target's id as it appears; the inbox stays gated until the tracker listens.
    private static ConcurrentDictionary<string, string> TrackReferences(IRenderedComponent<ApprovalInboxPage> page, params string[] selectors)
    {
        var references = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);
        void Record()
        {
            foreach (var selector in selectors)
            {
                var id = page.FindAll(selector).FirstOrDefault()?.GetAttribute("blazor:elementreference");
                if (!string.IsNullOrEmpty(id))
                {
                    references[selector] = id;
                }
            }
        }

        Record();
        page.OnMarkupUpdated += (_, _) => Record();
        return references;
    }

    // Find and Click run together on the renderer so a render from another thread cannot replace
    // the handler in between.
    private static void ClickCurrent(IRenderedComponent<ApprovalInboxPage> page, string selector) =>
        page.InvokeAsync(() => page.Find(selector).ClickAsync(new MouseEventArgs())).GetAwaiter().GetResult();

    private static void AssertFocused(JSRuntimeInvocation focus, ConcurrentDictionary<string, string> references, string selector)
    {
        Assert.True(references.TryGetValue(selector, out var expected), $"No element reference was recorded for {selector}.");
        Assert.Equal(expected, Assert.IsType<ElementReference>(focus.Arguments[0]).Id);
    }

    private static List<JSRuntimeInvocation> FocusCalls(BunitContext ctx) =>
        ctx.JSInterop.Invocations.Where(call => call.Identifier == FocusIdentifier).ToList();

    private sealed class GatedApprovalInboxClient(IConsoleApprovalInboxClient inner) : IConsoleApprovalInboxClient
    {
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Open() => _gate.TrySetResult();

        public async Task<OperateSectionResult<ApprovalInboxSnapshot>> GetInboxAsync(
            string? status = null, string? kind = null, CancellationToken cancellationToken = default)
        {
            await _gate.Task.WaitAsync(cancellationToken);
            return await inner.GetInboxAsync(status, kind, cancellationToken);
        }
    }
}
