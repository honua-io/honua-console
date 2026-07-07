using Bunit;
using Honua.Console.Contracts;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.Native.Core.Tests.Components;

/// <summary>
/// Coverage for the shared ops-summary strip (console#292 scope item 2; founder decision
/// 2026-07-07 ships it on both the operate hub and the console home): overall health, awaiting
/// approvals, open findings, and SLO breaches, each deep-linking to its actionable surface, with
/// honest degrade (never a fabricated zero) and a live/manual approvals indicator that never
/// claims a liveness it does not have.
/// </summary>
public sealed class OpsSummaryStripTests : ConsoleComponentTestBase
{
    [Fact]
    public void AllowedReads_RenderCountsAndLinkEachTileToItsSurface()
    {
        Services.AddSingleton<IOpsHealthDataSource>(new StubHealthDataSource
        {
            Result = OperateSectionResult<OpsHealthView>.Allowed(HealthyView() with
            {
                Overall = new OperateStatus("degraded", "One alert is firing."),
            }),
        });
        Services.AddSingleton<IConsoleOpsFindingsClient>(new StubFindingsClient
        {
            Result = OperateSectionResult<OpsFindingsListResponse>.Allowed(new OpsFindingsListResponse
            {
                Findings = [new OpsFindingResponse { Id = "f-1" }, new OpsFindingResponse { Id = "f-2" }],
            }),
        });
        Services.AddSingleton<IConsoleApprovalInboxClient>(new StubInboxClient
        {
            Result = OperateSectionResult<ApprovalInboxSnapshot>.Allowed(
                new ApprovalInboxSnapshot(
                [
                    new ApprovalInboxItem(ApprovalTicketType.PublishData, Proposal("p-1", ConsoleProposalStatus.AwaitingApproval)),
                ])),
        });
        Services.AddSingleton<IConsoleProposalRealtimeClient>(new StubRealtimeClient());

        var cut = Render<OpsSummaryStrip>();

        var healthTile = cut.Find("[data-summary-tile='health']");
        Assert.Equal("/operate/health", healthTile.GetAttribute("href"));
        Assert.Contains("degraded", cut.Find("[data-summary-tile='health'] .console-status").TextContent);

        Assert.Equal("1", cut.Find("[data-summary-value='approvals']").TextContent.Trim());
        Assert.Equal("/inbox", cut.Find("[data-summary-tile='approvals']").GetAttribute("href"));

        Assert.Equal("2", cut.Find("[data-summary-value='findings']").TextContent.Trim());
        Assert.Equal("/operate/copilot", cut.Find("[data-summary-tile='findings']").GetAttribute("href"));

        Assert.Equal("/operate/health", cut.Find("[data-summary-tile='breaches']").GetAttribute("href"));
    }

    [Fact]
    public void DeniedReads_RenderHonestUnavailableNeverAFabricatedZero()
    {
        Services.AddSingleton<IOpsHealthDataSource>(new StubHealthDataSource
        {
            Result = OperateSectionResult<OpsHealthView>.Denied(OperateSectionStatus.Unavailable, "n/a"),
        });
        Services.AddSingleton<IConsoleOpsFindingsClient>(new StubFindingsClient
        {
            Result = OperateSectionResult<OpsFindingsListResponse>.Denied(OperateSectionStatus.Unavailable, "n/a"),
        });
        Services.AddSingleton<IConsoleApprovalInboxClient>(new StubInboxClient
        {
            Result = OperateSectionResult<ApprovalInboxSnapshot>.Denied(OperateSectionStatus.Unavailable, "n/a"),
        });
        Services.AddSingleton<IConsoleProposalRealtimeClient>(new StubRealtimeClient());

        var cut = Render<OpsSummaryStrip>();

        Assert.Equal("Unavailable", cut.Find("[data-summary-unavailable='health']").TextContent.Trim());
        Assert.Equal("—", cut.Find("[data-summary-value='approvals']").TextContent.Trim());
        Assert.Equal("—", cut.Find("[data-summary-value='findings']").TextContent.Trim());
        Assert.Equal("—", cut.Find("[data-summary-value='breaches']").TextContent.Trim());
    }

    [Fact]
    public void RealtimeConnected_ShowsLiveNotManual()
    {
        Services.AddSingleton<IOpsHealthDataSource>(new StubHealthDataSource
        {
            Result = OperateSectionResult<OpsHealthView>.Allowed(HealthyView()),
        });
        Services.AddSingleton<IConsoleOpsFindingsClient>(new StubFindingsClient
        {
            Result = OperateSectionResult<OpsFindingsListResponse>.Allowed(new OpsFindingsListResponse { Findings = [] }),
        });
        Services.AddSingleton<IConsoleApprovalInboxClient>(new StubInboxClient
        {
            Result = OperateSectionResult<ApprovalInboxSnapshot>.Allowed(ApprovalInboxSnapshot.Empty),
        });
        Services.AddSingleton<IConsoleProposalRealtimeClient>(new StubRealtimeClient { Connected = true });

        var cut = Render<OpsSummaryStrip>();

        var liveness = cut.Find("[data-summary-liveness]");
        Assert.Contains("is-live", liveness.ClassList);
        Assert.Contains("Approvals live", liveness.TextContent);
    }

    [Fact]
    public async Task ProposalChangedEvent_RefreshesTheAwaitingApprovalsCountWithoutPolling()
    {
        var inbox = new StubInboxClient
        {
            Result = OperateSectionResult<ApprovalInboxSnapshot>.Allowed(ApprovalInboxSnapshot.Empty),
        };
        var realtime = new StubRealtimeClient();

        Services.AddSingleton<IOpsHealthDataSource>(new StubHealthDataSource
        {
            Result = OperateSectionResult<OpsHealthView>.Allowed(HealthyView()),
        });
        Services.AddSingleton<IConsoleOpsFindingsClient>(new StubFindingsClient
        {
            Result = OperateSectionResult<OpsFindingsListResponse>.Allowed(new OpsFindingsListResponse { Findings = [] }),
        });
        Services.AddSingleton<IConsoleApprovalInboxClient>(inbox);
        Services.AddSingleton<IConsoleProposalRealtimeClient>(realtime);

        var cut = Render<OpsSummaryStrip>();
        Assert.Equal("0", cut.Find("[data-summary-value='approvals']").TextContent.Trim());

        // A live pending-approval event arrives; the queue now has one awaiting item.
        inbox.Result = OperateSectionResult<ApprovalInboxSnapshot>.Allowed(
            new ApprovalInboxSnapshot([new ApprovalInboxItem(ApprovalTicketType.PublishData, Proposal("p-9", ConsoleProposalStatus.AwaitingApproval))]));
        await cut.InvokeAsync(() => realtime.RaiseProposalChanged(new ConsoleProposalEvent(
            ConsoleProposalEventKind.Pending,
            "p-9",
            ConsoleProposalKind.MetadataRelease,
            ConsoleProposalStatus.AwaitingApproval,
            "tester",
            ConsoleProposalRisk.Low,
            DateTimeOffset.Parse("2026-07-07T09:00:00Z"))));

        cut.WaitForAssertion(() =>
            Assert.Equal("1", cut.Find("[data-summary-value='approvals']").TextContent.Trim()));
    }

    private static ConsoleProposalSummary Proposal(string proposalId, ConsoleProposalStatus status) => new(
        ProposalId: proposalId,
        Kind: ConsoleProposalKind.MetadataRelease,
        Status: status,
        RequestedBy: "tester",
        RequestedByAgent: null,
        Summary: "Test proposal",
        RiskLevel: ConsoleProposalRisk.Low,
        CreatedAt: DateTimeOffset.Parse("2026-07-07T09:00:00Z"),
        UpdatedAt: DateTimeOffset.Parse("2026-07-07T09:00:00Z"));

    private static OpsHealthView HealthyView() => new(
        Overall: new OperateStatus("healthy", "All systems normal."),
        GeneratedAt: "2026-07-07 09:00 UTC",
        Health: new OpsHealthChecksView(new OperateStatus("healthy", ""), "10ms", []),
        ServingLatency: new OpsServingLatencyView("5 minutes", []),
        Geoprocessing: new OpsGpQueueView(0, true, new OperateStatus("healthy", ""), []),
        AlertDispatch: new OpsAlertDispatchView(new OperateStatus("healthy", ""), true, true, false, "n/a", "0", "0", false),
        Deploy: new OpsDeployReadinessView(
            new OperateStatus("healthy", ""), true, 0, 0,
            new OpsPlatformReleaseView("2026.07.1", true, true, new OperateStatus("healthy", ""), [])),
        Database: new OpsDatabaseView(
            new OperateMetricBar(10, "10%", new OperateStatus("healthy", ""), true),
            new OperateMetricBar(95, "95%", new OperateStatus("healthy", ""), true),
            new OperateMetricBar(0, "0%", new OperateStatus("healthy", ""), true),
            5, 0, 0));

    private sealed class StubHealthDataSource : IOpsHealthDataSource
    {
        public OperateSectionResult<OpsHealthView> Result { get; set; } =
            OperateSectionResult<OpsHealthView>.Denied(OperateSectionStatus.Unavailable, "n/a");

        public Task<OperateSectionResult<OpsHealthView>> GetSnapshotAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Result);
    }

    private sealed class StubFindingsClient : IConsoleOpsFindingsClient
    {
        public OperateSectionResult<OpsFindingsListResponse> Result { get; set; } =
            OperateSectionResult<OpsFindingsListResponse>.Denied(OperateSectionStatus.Unavailable, "n/a");

        public Task<OperateSectionResult<OpsFindingsListResponse>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Result);

        public Task<OperateSectionResult<OpsFindingProposeResponse>> ProposeAsync(string findingId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by the summary strip.");
    }

    private sealed class StubInboxClient : IConsoleApprovalInboxClient
    {
        public OperateSectionResult<ApprovalInboxSnapshot> Result { get; set; } =
            OperateSectionResult<ApprovalInboxSnapshot>.Denied(OperateSectionStatus.Unavailable, "n/a");

        public Task<OperateSectionResult<ApprovalInboxSnapshot>> GetInboxAsync(
            string? status = null, string? kind = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result);
    }

    private sealed class StubRealtimeClient : IConsoleProposalRealtimeClient
    {
        public bool Connected { get; set; }

        public bool IsConnected => Connected;

        public event Action<ConsoleProposalEvent>? ProposalChanged;

        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void RaiseProposalChanged(ConsoleProposalEvent evt) => ProposalChanged?.Invoke(evt);
    }
}
