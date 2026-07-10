using Bunit;
using Microsoft.AspNetCore.Components;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free bUnit coverage for the deploy-operations list (console#290 acceptance
/// criterion 1): consumes <see cref="IConsoleDeployOperationsClient"/> directly (the real
/// paged list, honua-server PR #2577) rather than the retired release-scrape workaround, and
/// renders the honest Manual pill when the deploy-operations realtime seam
/// (<see cref="IConsoleDeployOperationRealtimeClient"/>) is not registered — exactly the state
/// every server has today (honua-server#2554 not yet merged).
/// </summary>
public sealed class OperateDeployOperationsListTests
{
    [Fact]
    public void RendersRowsFromRealListClient_AndShowsManualPill_WhenRealtimeNotRegistered()
    {
        var client = new InMemoryConsoleDeployOperationsClient([Awaiting("op-1"), Awaiting("op-2")]);

        using var ctx = new BunitContext();
        ctx.Services.AddSingleton<IConsoleDeployOperationsClient>(client);

        var list = ctx.Render<OperateDeployOperationsList>();

        list.WaitForAssertion(
            () =>
            {
                Assert.Equal(2, list.FindAll(".operate-deploy-proposal-row").Count);
                Assert.Equal("Manual", list.Find("[data-realtime-pill]").TextContent.Trim());
            },
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Unsupported_RendersHonestUnsupportedSurface_ForOlderServer()
    {
        // Simulate an older server: the list route itself is unsupported.
        var unsupportedClient = new UnsupportedListClient();

        using var ctx = new BunitContext();
        ctx.Services.AddSingleton<IConsoleDeployOperationsClient>(unsupportedClient);

        var list = ctx.Render<OperateDeployOperationsList>();

        list.WaitForAssertion(
            () => Assert.Contains("not available on the connected server", list.Markup, StringComparison.OrdinalIgnoreCase),
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void SelectingRow_RaisesOnSelect()
    {
        DeployOperationProposal? selected = null;
        var client = new InMemoryConsoleDeployOperationsClient([Awaiting("op-selectable")]);

        using var ctx = new BunitContext();
        ctx.Services.AddSingleton<IConsoleDeployOperationsClient>(client);

        var list = ctx.Render<OperateDeployOperationsList>(p => p
            .Add(x => x.OnSelect, EventCallback.Factory.Create<DeployOperationProposal>(this, p2 => selected = p2)));

        list.WaitForAssertion(
            () => Assert.NotNull(list.Find("button.operate-deploy-proposal-select")),
            TimeSpan.FromSeconds(5));

        list.Find("button.operate-deploy-proposal-select").Click();

        Assert.NotNull(selected);
        Assert.Equal("op-selectable", selected!.OperationId);
    }

    private static DeployOperationProposal Awaiting(string operationId) => new(
        OperationId: operationId,
        Lifecycle: DeployOperationLifecycle.AwaitingApproval,
        RawStatus: "AwaitingApproval",
        Kind: "Deploy",
        Priority: "normal",
        Service: "honua-server",
        Environment: "staging",
        DesiredRevision: "1.5.0",
        CurrentRevision: "1.4.2",
        Action: "deploy",
        ChangeSummary: "Upgrade honua-server to 1.5.0.",
        RequestedBy: "honua-devops",
        Reason: null,
        PrUrl: null,
        CommitSha: null,
        Evidence: [],
        RollbackPlan: null,
        Warnings: [],
        BlockingReasons: [],
        CreatedAt: DateTimeOffset.UtcNow.AddMinutes(-3),
        UpdatedAt: DateTimeOffset.UtcNow.AddMinutes(-1));

    private sealed class UnsupportedListClient : IConsoleDeployOperationsClient
    {
        public Task<OperateSectionResult<DeployOperationListView>> ListAsync(
            DeployOperationListQuery query,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<DeployOperationListView>.Denied(
                OperateSectionStatus.Unsupported,
                "The honua-server deploy-operations list API is not available on the connected server."));

        public Task<OperateSectionResult<DeployPreflightView>> GetPreflightAsync(
            bool includeDiagnostics = true,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<DeployPreflightView>.Denied(OperateSectionStatus.Unsupported, "n/a"));

        public Task<OperateSectionResult<PlatformReleaseConvergeView>> ConvergeAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<PlatformReleaseConvergeView>.Denied(OperateSectionStatus.Unsupported, "n/a"));
    }
}
