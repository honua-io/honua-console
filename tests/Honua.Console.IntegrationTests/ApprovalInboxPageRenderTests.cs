using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free render regression for the approval inbox (#193) — the GIS-department work
/// queue bound to the first-class honua-server proposals API (#1694). A live page binds to a
/// server (charter §11); these tests drive it through a fake proposals client to assert:
///   - a denied proposals read renders the shared missing/forbidden surface,
///   - an empty queue renders the honest empty state and zero counts,
///   - a populated queue renders summary counts, ticket-type filter chips, and rows,
///   - selecting a row binds the governed approval panel (plan/diff/risk),
///   - approve / reject (reason required) call the server and refresh the queue,
///   - a 403 (missing RBAC approve grant) disables the actions and surfaces the gate, and
///   - a live ProposalPending event refreshes the queue without polling.
/// </summary>
public sealed class ApprovalInboxPageRenderTests
{
    private static BunitContext NewContext(
        FakeConsoleProposalsClient proposals,
        FakeConsoleProposalRealtimeClient? realtime = null)
    {
        var ctx = new BunitContext();
        ctx.Services.AddSingleton<IConsoleProposalsClient>(proposals);
        ctx.Services.AddSingleton<IConsoleApprovalInboxClient>(new ConsoleApprovalInboxClient(proposals));
        ctx.Services.AddSingleton<IConsoleProposalRealtimeClient>(realtime ?? new FakeConsoleProposalRealtimeClient());
        return ctx;
    }

    // Bounded-loading / trust-state tests (console#308) drive the page's own timeout, error, and
    // last-refresh logic directly through the inbox client, without routing through the aggregator.
    private static BunitContext NewContextWithInbox(
        IConsoleApprovalInboxClient inbox,
        FakeConsoleProposalRealtimeClient? realtime = null)
    {
        var ctx = new BunitContext();
        ctx.Services.AddSingleton<IConsoleApprovalInboxClient>(inbox);
        ctx.Services.AddSingleton<IConsoleProposalRealtimeClient>(realtime ?? new FakeConsoleProposalRealtimeClient());
        return ctx;
    }

    [Fact]
    public void DeniedRead_RendersStatusSurface()
    {
        using var ctx = NewContext(new FakeConsoleProposalsClient(
            deniedListStatus: OperateSectionStatus.Unavailable,
            deniedListMessage: "No active environment profile is selected."));

        var page = ctx.Render<ApprovalInboxPage>();

        page.WaitForAssertion(
            () => Assert.Contains("No active environment profile is selected.", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void EverySourceUnavailable_RendersServerBindingError_NotAnEmptyQueue()
    {
        const string serverMessage = "No active environment profile is selected.";
        var proposals = new FakeConsoleProposalsClient(
            deniedListStatus: OperateSectionStatus.Unavailable,
            deniedListMessage: serverMessage);
        using var ctx = new BunitContext();
        ctx.Services.AddSingleton<IConsoleApprovalInboxClient>(
            new ConsoleApprovalInboxClient(
            [
                new ServerConsoleProposalSource(proposals),
                new DevOpsConsoleProposalSource(new UnavailableConsoleDevOpsProposalsClient()),
            ]));
        ctx.Services.AddSingleton<IConsoleProposalRealtimeClient>(new FakeConsoleProposalRealtimeClient());

        var page = ctx.Render<ApprovalInboxPage>();

        page.WaitForAssertion(
            () =>
            {
                Assert.Contains(serverMessage, page.Markup, StringComparison.Ordinal);
                Assert.DoesNotContain("No work in the queue", page.Markup, StringComparison.Ordinal);
                Assert.DoesNotContain("data-awaiting-count", page.Markup, StringComparison.Ordinal);
            },
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void ReachableServerWithUnavailableDevOps_RendersServerQueueAsPartial()
    {
        var proposals = new FakeConsoleProposalsClient(
            proposals:
            [
                FakeProposalFactory.Summary(
                    "server-op",
                    ConsoleProposalKind.MetadataRelease,
                    summary: "Promote parcels")
            ]);
        using var ctx = new BunitContext();
        ctx.Services.AddSingleton<IConsoleApprovalInboxClient>(
            new ConsoleApprovalInboxClient(
            [
                new ServerConsoleProposalSource(proposals),
                new DevOpsConsoleProposalSource(new UnavailableConsoleDevOpsProposalsClient()),
            ]));
        ctx.Services.AddSingleton<IConsoleProposalRealtimeClient>(new FakeConsoleProposalRealtimeClient());

        var page = ctx.Render<ApprovalInboxPage>();

        page.WaitForAssertion(
            () =>
            {
                Assert.NotNull(page.Find("[data-proposal-id=\"server-op\"]"));
                Assert.Equal("1", page.Find("[data-awaiting-count] strong").TextContent.Trim());
                Assert.Contains("DevOps proposal source is unavailable", page.Markup, StringComparison.Ordinal);
            },
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void EmptyQueue_RendersHonestEmptyState_AndZeroCounts()
    {
        using var ctx = NewContext(new FakeConsoleProposalsClient(proposals: []));

        var page = ctx.Render<ApprovalInboxPage>();

        page.WaitForAssertion(
            () =>
            {
                Assert.Equal("0", page.Find("[data-awaiting-count] strong").TextContent.Trim());
                Assert.Contains("No work in the queue", page.Markup, StringComparison.Ordinal);
            },
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void PopulatedQueue_RendersClassifiedRow_AndSelectingItOpensApprovalPanel()
    {
        var proposals = new FakeConsoleProposalsClient(
            proposals: [FakeProposalFactory.Summary("promo-op", ConsoleProposalKind.MetadataRelease, summary: "Promote parcels")],
            details: [FakeProposalFactory.Detail("promo-op", ConsoleProposalKind.MetadataRelease, summary: "Promote parcels", diff: ["+ field parcels.zoning"])]);
        using var ctx = NewContext(proposals);

        var page = ctx.Render<ApprovalInboxPage>();

        page.WaitForAssertion(
            () =>
            {
                var row = page.Find("[data-proposal-id=\"promo-op\"]");
                Assert.Contains("Publish / update data", row.InnerHtml, StringComparison.Ordinal);
                Assert.Equal("1", page.Find("[data-awaiting-count] strong").TextContent.Trim());
            },
            TimeSpan.FromSeconds(5));

        page.Find("[data-proposal-id=\"promo-op\"]").Click();
        page.WaitForAssertion(
            () =>
            {
                Assert.Contains("Plan &amp; diff", page.Markup, StringComparison.Ordinal);
                Assert.Contains("+ field parcels.zoning", page.Markup, StringComparison.Ordinal);
            },
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void DataImportProposal_RendersImportCard_WithPlanAndRisk()
    {
        var proposals = new FakeConsoleProposalsClient(
            proposals: [FakeProposalFactory.Summary("import-op", ConsoleProposalKind.DataImport, summary: "Import parcels.gpkg")],
            details:
            [
                FakeProposalFactory.Detail("import-op", ConsoleProposalKind.DataImport, summary: "Import parcels.gpkg",
                    diff: ["+ layer parcels", "+ 12,345 features"],
                    dryRun: ["estimated 12s"],
                    warnings: ["CRS assumed EPSG:4326"])
            ]);
        using var ctx = NewContext(proposals);

        var page = ctx.Render<ApprovalInboxPage>();
        page.WaitForAssertion(() => page.Find("[data-proposal-id=\"import-op\"]"), TimeSpan.FromSeconds(5));
        page.Find("[data-proposal-id=\"import-op\"]").Click();

        page.WaitForAssertion(
            () =>
            {
                Assert.Contains("Data import approval", page.Markup, StringComparison.Ordinal);
                Assert.Contains("Import plan &amp; diff", page.Markup, StringComparison.Ordinal);
                Assert.Contains("+ 12,345 features", page.Markup, StringComparison.Ordinal);
                Assert.Contains("CRS assumed EPSG:4326", page.Markup, StringComparison.Ordinal);
            },
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Approve_CallsServer_AndDropsResolvedProposalFromAwaiting()
    {
        var proposals = new FakeConsoleProposalsClient(
            proposals: [FakeProposalFactory.Summary("op-1", ConsoleProposalKind.Deploy, summary: "Upgrade to v21")],
            details: [FakeProposalFactory.Detail("op-1", ConsoleProposalKind.Deploy, summary: "Upgrade to v21")]);
        using var ctx = NewContext(proposals);

        var page = ctx.Render<ApprovalInboxPage>();
        page.WaitForAssertion(() => page.Find("[data-proposal-id=\"op-1\"]"), TimeSpan.FromSeconds(5));
        page.Find("[data-proposal-id=\"op-1\"]").Click();

        page.WaitForAssertion(() => page.Find("[data-proposal-approve]"), TimeSpan.FromSeconds(5));
        page.Find("[data-proposal-approve]").Click();

        page.WaitForAssertion(
            () =>
            {
                Assert.Contains("op-1", proposals.Approved);
                // The now-Submitted proposal is no longer awaiting; the awaiting count drops.
                Assert.Equal("0", page.Find("[data-awaiting-count] strong").TextContent.Trim());
            },
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Reject_RequiresReason_ThenCallsServerWithIt()
    {
        var proposals = new FakeConsoleProposalsClient(
            proposals: [FakeProposalFactory.Summary("op-2", ConsoleProposalKind.Deploy, summary: "Upgrade to v21")],
            details: [FakeProposalFactory.Detail("op-2", ConsoleProposalKind.Deploy, summary: "Upgrade to v21")]);
        using var ctx = NewContext(proposals);

        var page = ctx.Render<ApprovalInboxPage>();
        page.WaitForAssertion(() => page.Find("[data-proposal-id=\"op-2\"]"), TimeSpan.FromSeconds(5));
        page.Find("[data-proposal-id=\"op-2\"]").Click();

        // The reject button is disabled until a reason is entered.
        page.WaitForAssertion(
            () => Assert.True(page.Find("[data-proposal-reject]").HasAttribute("disabled")),
            TimeSpan.FromSeconds(5));

        page.Find("[data-proposal-reason]").Change("Out of change window");
        page.WaitForAssertion(
            () => Assert.False(page.Find("[data-proposal-reject]").HasAttribute("disabled")),
            TimeSpan.FromSeconds(5));

        page.Find("[data-proposal-reject]").Click();
        page.WaitForAssertion(
            () =>
            {
                var rejected = Assert.Single(proposals.Rejected);
                Assert.Equal("op-2", rejected.ProposalId);
                Assert.Equal("Out of change window", rejected.Reason);
            },
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void ForbiddenApprove_DisablesActions_AndSurfacesTheApproveGate()
    {
        var proposals = new FakeConsoleProposalsClient(
            proposals: [FakeProposalFactory.Summary("op-3", ConsoleProposalKind.Deploy, summary: "Upgrade to v21")],
            details: [FakeProposalFactory.Detail("op-3", ConsoleProposalKind.Deploy, summary: "Upgrade to v21")],
            approveForbidden: true);
        using var ctx = NewContext(proposals);

        var page = ctx.Render<ApprovalInboxPage>();
        page.WaitForAssertion(() => page.Find("[data-proposal-id=\"op-3\"]"), TimeSpan.FromSeconds(5));
        page.Find("[data-proposal-id=\"op-3\"]").Click();

        page.WaitForAssertion(() => page.Find("[data-proposal-approve]"), TimeSpan.FromSeconds(5));
        page.Find("[data-proposal-approve]").Click();

        page.WaitForAssertion(
            () =>
            {
                Assert.NotNull(page.Find("[data-proposal-rbac-denied]"));
                Assert.True(page.Find("[data-proposal-approve]").HasAttribute("disabled"));
            },
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void LivePendingEvent_RefreshesTheQueue_WithoutPolling()
    {
        // Start empty; the realtime client later announces a new pending proposal, and the
        // page re-reads the list (the fake returns the now-populated list).
        var realtime = new FakeConsoleProposalRealtimeClient();
        var seeded = new[] { FakeProposalFactory.Summary("late-op", ConsoleProposalKind.MetadataRelease, summary: "Promote roads") };
        var proposals = new GrowingProposalsClient(seeded);
        var ctx = new BunitContext();
        ctx.Services.AddSingleton<IConsoleProposalsClient>(proposals);
        ctx.Services.AddSingleton<IConsoleApprovalInboxClient>(new ConsoleApprovalInboxClient(proposals));
        ctx.Services.AddSingleton<IConsoleProposalRealtimeClient>(realtime);

        var page = ctx.Render<ApprovalInboxPage>();
        page.WaitForAssertion(
            () => Assert.Equal("0", page.Find("[data-total-count] strong").TextContent.Trim()),
            TimeSpan.FromSeconds(5));
        Assert.True(realtime.StartCount > 0);

        proposals.Reveal();
        realtime.Raise(new ConsoleProposalEvent(
            ConsoleProposalEventKind.Pending, "late-op", ConsoleProposalKind.MetadataRelease,
            ConsoleProposalStatus.AwaitingApproval, "agent", ConsoleProposalRisk.Low, DateTimeOffset.UtcNow));

        page.WaitForAssertion(
            () =>
            {
                Assert.Equal("1", page.Find("[data-total-count] strong").TextContent.Trim());
                Assert.NotNull(page.Find("[data-proposal-id=\"late-op\"]"));
            },
            TimeSpan.FromSeconds(5));

        ctx.Dispose();
    }

    // ── Bounded loading + explicit error/retry/last-refreshed (console#308) ──────────────────

    [Fact]
    public void HungRead_BoundedLoading_ResolvesToErrorCard_WithRetryAndNeverLoaded()
    {
        // A backend that never answers must not spin on "Loading…" forever: the ~5s budget
        // (driven small here) resolves to the explicit error card, distinct from the empty state.
        var ctx = NewContextWithInbox(new HangingApprovalInboxClient());

        var page = ctx.Render<ApprovalInboxPage>(p => p.Add(x => x.LoadBudget, TimeSpan.FromMilliseconds(150)));

        page.WaitForAssertion(
            () =>
            {
                var error = page.Find("[data-inbox-error]");
                Assert.Contains("Couldn't read the approval queue", error.TextContent, StringComparison.Ordinal);
                Assert.NotNull(page.Find("[data-inbox-retry]"));
                Assert.Contains("Never loaded", page.Find("[data-inbox-last-refreshed]").TextContent, StringComparison.Ordinal);
                // The error state is not the empty-success state.
                Assert.DoesNotContain("No work in the queue", page.Markup, StringComparison.Ordinal);
            },
            TimeSpan.FromSeconds(5));

        ctx.Dispose();
    }

    [Fact]
    public void Retry_AfterError_RecoversToQueue()
    {
        var recovering = new RecoveringApprovalInboxClient(
            failuresBeforeSuccess: 1,
            onRecovery: FakeProposalFactory.Snapshot(
                FakeProposalFactory.Summary("recovered-op", ConsoleProposalKind.MetadataRelease, summary: "Promote roads")));
        var ctx = NewContextWithInbox(recovering);

        var page = ctx.Render<ApprovalInboxPage>();

        // The first read failed: error card with a working Retry.
        page.WaitForAssertion(() => Assert.NotNull(page.Find("[data-inbox-retry]")), TimeSpan.FromSeconds(5));

        page.Find("[data-inbox-retry]").Click();

        // Retry re-reads; the now-recovered backend returns the queue.
        page.WaitForAssertion(
            () =>
            {
                Assert.NotNull(page.Find("[data-proposal-id=\"recovered-op\"]"));
                Assert.Equal("1", page.Find("[data-total-count] strong").TextContent.Trim());
            },
            TimeSpan.FromSeconds(5));

        ctx.Dispose();
    }

    [Fact]
    public void ErrorAfterSuccess_KeepsLastSuccessfulRefresh_NotNever()
    {
        var scripted = new ScriptedApprovalInboxClient
        {
            Result = OperateSectionResult<ApprovalInboxSnapshot>.Allowed(ApprovalInboxSnapshot.Empty),
        };
        var ctx = NewContextWithInbox(scripted);

        var page = ctx.Render<ApprovalInboxPage>();

        // Initial success renders the empty state (distinct from an error) and stamps the marker.
        page.WaitForAssertion(
            () => Assert.Contains("No work in the queue", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        // The backend now fails; a manual refresh hits the error card, which must keep the last
        // good refresh time rather than reverting to "never".
        scripted.Result = OperateSectionResult<ApprovalInboxSnapshot>.Denied(
            OperateSectionStatus.Unavailable, "The honua-server admin API returned 500.");
        page.Find("[data-refresh-inbox]").Click();

        page.WaitForAssertion(
            () =>
            {
                var refreshed = page.Find("[data-inbox-last-refreshed]").TextContent;
                Assert.Contains("Last refreshed", refreshed, StringComparison.Ordinal);
                Assert.DoesNotContain("Never loaded", refreshed, StringComparison.Ordinal);
            },
            TimeSpan.FromSeconds(5));

        ctx.Dispose();
    }

    // ── Freshness affordance: Live / paused / reconnect (console#309) ────────────────────────

    [Fact]
    public void Freshness_Connected_ShowsLive()
    {
        var ctx = NewContext(new FakeConsoleProposalsClient(proposals: []));

        var page = ctx.Render<ApprovalInboxPage>();

        page.WaitForAssertion(
            () =>
            {
                var pill = page.Find("[data-live-state]");
                Assert.Equal("live", pill.GetAttribute("data-live-state"));
                Assert.Contains("Live", pill.TextContent, StringComparison.Ordinal);
            },
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Freshness_Degraded_ShowsPausedWithTimestamp_AndRefresh()
    {
        var realtime = new FakeConsoleProposalRealtimeClient { ConnectOnStart = false };
        var ctx = NewContext(new FakeConsoleProposalsClient(proposals: []), realtime);

        var page = ctx.Render<ApprovalInboxPage>();

        page.WaitForAssertion(
            () =>
            {
                var pill = page.Find("[data-live-state]");
                Assert.Equal("paused", pill.GetAttribute("data-live-state"));
                Assert.Contains("Updates paused", pill.TextContent, StringComparison.Ordinal);
                // The successful (empty) read stamped a last-refreshed time, not "not yet".
                Assert.Contains("UTC", page.Find("[data-inbox-paused-since]").TextContent, StringComparison.Ordinal);
                Assert.NotNull(page.Find("[data-refresh-inbox]"));
            },
            TimeSpan.FromSeconds(5));

        ctx.Dispose();
    }

    [Fact]
    public void Freshness_Reconnect_FlipsPausedToLive_WithoutReload()
    {
        var realtime = new FakeConsoleProposalRealtimeClient { ConnectOnStart = false };
        var ctx = NewContext(new FakeConsoleProposalsClient(proposals: []), realtime);

        var page = ctx.Render<ApprovalInboxPage>();
        page.WaitForAssertion(
            () => Assert.Equal("paused", page.Find("[data-live-state]").GetAttribute("data-live-state")),
            TimeSpan.FromSeconds(5));

        // The hub reconnects; the pill flips to Live with no manual refresh and no page reload.
        page.InvokeAsync(() => realtime.SetConnectionState(ConsoleRealtimeConnectionState.Connected));

        page.WaitForAssertion(
            () =>
            {
                var pill = page.Find("[data-live-state]");
                Assert.Equal("live", pill.GetAttribute("data-live-state"));
                Assert.Contains("Live", pill.TextContent, StringComparison.Ordinal);
            },
            TimeSpan.FromSeconds(5));

        ctx.Dispose();
    }

    // A proposals client that returns an empty list until Reveal() is called, then returns the
    // seeded list — to model a proposal that appears between the initial read and a live event.
    private sealed class GrowingProposalsClient(IReadOnlyList<ConsoleProposalSummary> revealed) : IConsoleProposalsClient
    {
        private bool _revealed;

        public void Reveal() => _revealed = true;

        public Task<OperateSectionResult<IReadOnlyList<ConsoleProposalSummary>>> ListAsync(
            string? status = null, string? kind = null, string? requestedBy = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<IReadOnlyList<ConsoleProposalSummary>>.Allowed(
                _revealed ? revealed : []));

        public Task<OperateSectionResult<ConsoleProposalDetail>> GetAsync(
            string proposalId, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<ConsoleProposalDetail>.Denied(OperateSectionStatus.Missing, "n/a"));

        public Task<OperateSectionResult<ConsoleProposalDetail>> ApproveAsync(
            string proposalId, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<ConsoleProposalDetail>.Denied(OperateSectionStatus.Missing, "n/a"));

        public Task<OperateSectionResult<ConsoleProposalDetail>> RejectAsync(
            string proposalId, string reason, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<ConsoleProposalDetail>.Denied(OperateSectionStatus.Missing, "n/a"));
    }
}
