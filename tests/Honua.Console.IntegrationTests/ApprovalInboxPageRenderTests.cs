using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Honua.Console.IntegrationTests;

public sealed class ApprovalInboxPageRenderTests
{
    [Fact]
    public async Task Prerender_StalledRealtime_CompletesWithTheRestQueue()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var realtime = new FakeConsoleProposalRealtimeClient { StartOperation = _ => release.Task };
        await using var ctx = NewContext(new FakeConsoleProposalsClient(
            proposals: [FakeProposalFactory.Summary("rest-proposal", ConsoleProposalKind.Deploy)]), realtime);
        await using var renderer = new HtmlRenderer(ctx.Services, ctx.Services.GetRequiredService<ILoggerFactory>());

        try
        {
            var rendering = renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<ApprovalInboxPage>(
                ParameterView.FromDictionary(new Dictionary<string, object>
                {
                    [nameof(ApprovalInboxPage.LoadBudget)] = TimeSpan.FromMilliseconds(50),
                })));
            var output = await rendering.WaitAsync(TimeSpan.FromSeconds(2));
            var html = await renderer.Dispatcher.InvokeAsync(output.ToHtmlString);

            Assert.Contains("rest-proposal", html, StringComparison.Ordinal);
            Assert.Contains("data-live-state=\"paused\"", html, StringComparison.Ordinal);
        }
        finally
        {
            release.TrySetResult();
        }
    }

    [Fact]
    public async Task RealtimeStartup_ExceedsReadBudget_CancelsAndKeepsRestApprovalUsable()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var startupToken = CancellationToken.None;
        var realtime = new FakeConsoleProposalRealtimeClient
        {
            StartOperation = async token =>
            {
                startupToken = token;
                try { await release.Task.WaitAsync(token); }
                finally { stopped.TrySetResult(); }
            },
        };
        var proposals = new FakeConsoleProposalsClient(
            proposals: [FakeProposalFactory.Summary("rest-proposal", ConsoleProposalKind.Deploy)],
            details: [FakeProposalFactory.Detail("rest-proposal", ConsoleProposalKind.Deploy)]);
        await using var ctx = NewContext(proposals, realtime);

        try
        {
            var page = ctx.Render<ApprovalInboxPage>(p => p.Add(x => x.LoadBudget, TimeSpan.FromMilliseconds(50)));
            await stopped.Task.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.True(startupToken.IsCancellationRequested);
            page.WaitForAssertion(() => Assert.Equal("paused", page.Find("[data-live-state]").GetAttribute("data-live-state")));
            page.Find("[data-proposal-id=\"rest-proposal\"]").Click();
            page.WaitForAssertion(() => page.Find("[data-proposal-approve]"));
            page.Find("[data-proposal-approve]").Click();
            page.WaitForAssertion(() => Assert.Equal("rest-proposal", Assert.Single(proposals.Approved)));
            Assert.Equal(0, realtime.StopCount);
        }
        finally
        {
            release.TrySetResult();
        }
    }

    [Fact]
    public async Task Dispose_PendingRealtimeStartup_CancelsAndCompletes()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var realtime = new FakeConsoleProposalRealtimeClient
        {
            StartOperation = async token =>
            {
                entered.TrySetResult();
                try { await release.Task.WaitAsync(token); }
                finally { stopped.TrySetResult(); }
            },
        };
        var ctx = NewContext(new FakeConsoleProposalsClient(proposals: []), realtime);

        try
        {
            ctx.Render<ApprovalInboxPage>();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await ctx.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            await stopped.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(0, realtime.StopCount);
        }
        finally
        {
            release.TrySetResult();
            await ctx.DisposeAsync();
        }
    }

    private static BunitContext NewContext(
        FakeConsoleProposalsClient proposals,
        FakeConsoleProposalRealtimeClient? realtime = null,
        Action<BunitContext>? registerServices = null)
    {
        var ctx = new BunitContext();
        ctx.Services.AddSingleton<IConsoleProposalsClient>(proposals);
        ctx.Services.AddSingleton<IConsoleApprovalInboxClient>(new ConsoleApprovalInboxClient(proposals));
        ctx.Services.AddSingleton<IConsoleProposalRealtimeClient>(realtime ?? new FakeConsoleProposalRealtimeClient());
        registerServices?.Invoke(ctx);
        ctx.SetRendererInfo(new RendererInfo("Server", isInteractive: true));
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
        ctx.SetRendererInfo(new RendererInfo("Server", isInteractive: true));
        return ctx;
    }

    [Theory]
    [InlineData("op-1", false, "manager:op-1")]
    [InlineData("op-1", true, "op-1")]
    [InlineData("manager:op-1", true, "manager:op-1")]
    public void DeepLink_SelectsManagerFallbackOnlyWhenNoExactMatch(string queryId, bool hasServing, string expectedId)
    {
        var managerSummary = FakeProposalFactory.Summary("manager:op-1", ConsoleProposalKind.Deploy);
        var managerDetail = FakeProposalFactory.Detail("manager:op-1", ConsoleProposalKind.Deploy);
        var proposals = new FakeConsoleProposalsClient(
            proposals: hasServing ? [managerSummary, FakeProposalFactory.Summary("op-1", ConsoleProposalKind.Deploy)] : [managerSummary],
            details: hasServing ? [managerDetail, FakeProposalFactory.Detail("op-1", ConsoleProposalKind.Deploy)] : [managerDetail]);
        using var ctx = NewContext(proposals);
        ctx.Services.GetRequiredService<NavigationManager>().NavigateTo("/inbox?proposalId=" + Uri.EscapeDataString(queryId));

        var page = ctx.Render<ApprovalInboxPage>();
        page.WaitForAssertion(() => page.Find("[data-proposal-approve]"), TimeSpan.FromSeconds(5));
        page.Find("[data-proposal-approve]").Click();
        page.WaitForAssertion(() => Assert.Equal(expectedId, Assert.Single(proposals.Approved)), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void SeparateManager_DisclosesServingRefreshRequirement()
    {
        using var ctx = NewContext(new FakeConsoleProposalsClient(proposals: []), registerServices: context =>
        {
            context.Services.AddSingleton<IConsoleEnvironmentProfileStore>(new InMemoryConsoleEnvironmentProfileStore(
                [new ConsoleEnvironmentProfile { Id = "local", ServerBaseUri = new Uri("https://serving.example/") }], activeProfileId: "local"));
            context.Services.AddSingleton<IConsoleManagerEndpoint>(new ManagerEndpoint());
        });

        var page = ctx.Render<ApprovalInboxPage>();
        page.WaitForAssertion(() => Assert.Contains("Manager live — refresh for serving updates",
            page.Find("[data-live-state]").TextContent, StringComparison.Ordinal), TimeSpan.FromSeconds(5));
    }

    private sealed class ManagerEndpoint : IConsoleManagerEndpoint
    {
        public Uri? Resolve(Uri servingBaseUri) => new("https://manager.example/");
    }

    [Fact]
    public async Task ExternalApproval_RetainsSelectedProposalAndShowsCompletion()
    {
        var proposals = new FakeConsoleProposalsClient(
            proposals: [FakeProposalFactory.Summary("external-1", ConsoleProposalKind.Deploy)],
            details: [FakeProposalFactory.Detail("external-1", ConsoleProposalKind.Deploy)]);
        var realtime = new FakeConsoleProposalRealtimeClient();
        using var ctx = NewContext(proposals, realtime);
        var page = ctx.Render<ApprovalInboxPage>(parameters => parameters.Add(p => p.DetailRefreshInterval, TimeSpan.FromMilliseconds(30)));
        page.WaitForAssertion(() => page.Find("[data-proposal-id=\"external-1\"]"), TimeSpan.FromSeconds(5));
        page.Find("[data-proposal-id=\"external-1\"]").Click();
        page.WaitForAssertion(() => page.Find("[data-proposal-approve]"), TimeSpan.FromSeconds(5));
        await proposals.ApproveAsync("external-1");
        proposals.RemoveFromQueue("external-1");
        realtime.Raise(new ConsoleProposalEvent(ConsoleProposalEventKind.Resolved, "external-1",
            ConsoleProposalKind.Deploy, ConsoleProposalStatus.Submitted, null, ConsoleProposalRisk.Medium, DateTimeOffset.UtcNow));
        page.WaitForAssertion(() => Assert.Equal("Submitted", page.Find("[data-proposal-status]").GetAttribute("data-proposal-status")), TimeSpan.FromSeconds(5));
        proposals.Complete("external-1");
        page.WaitForAssertion(() => Assert.Equal("Completed", page.Find("[data-proposal-status]").GetAttribute("data-proposal-status")), TimeSpan.FromSeconds(5));
        var reads = proposals.DetailReads;
        await Task.Delay(180);
        Assert.Equal(reads, proposals.DetailReads);
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

        ctx.SetRendererInfo(new RendererInfo("Server", isInteractive: true));
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
