using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.Native.Core.Tests;

/// <summary>
/// Pins the approval-inbox (#193) projection: the GIS-desk ticket-type classification
/// (MetadataRelease → publish data, Deploy/Rollback → server upgrade, else other), the
/// snapshot helpers (counts, present types, filter), and the aggregator's honest
/// missing-binding pass-through. The aggregator introduces no server contract; it composes
/// the GitOps-release and deploy-control clients, so a denied deploy-control read surfaces
/// as a denied inbox read rather than a fabricated queue.
/// </summary>
public sealed class ApprovalInboxTests
{
    private static DeployOperationProposal Proposal(
        string operationId,
        string kind,
        DeployOperationLifecycle lifecycle = DeployOperationLifecycle.AwaitingApproval,
        DateTimeOffset? updatedAt = null) => new(
        OperationId: operationId,
        Lifecycle: lifecycle,
        RawStatus: lifecycle.ToString(),
        Kind: kind,
        Priority: "normal",
        Service: kind,
        Environment: "prod",
        DesiredRevision: "rev-1",
        CurrentRevision: null,
        Action: "deploy",
        ChangeSummary: "summary",
        RequestedBy: null,
        Reason: null,
        PrUrl: null,
        CommitSha: null,
        Evidence: [],
        RollbackPlan: null,
        Warnings: [],
        BlockingReasons: [],
        CreatedAt: DateTimeOffset.UtcNow,
        UpdatedAt: updatedAt ?? DateTimeOffset.UtcNow);

    [Fact]
    public void Classify_MapsKindsOntoTicketTypes()
    {
        Assert.Equal(ApprovalTicketType.PublishData, ApprovalTicketPresentation.Classify(Proposal("a", "MetadataRelease")));
        Assert.Equal(ApprovalTicketType.ServerUpgrade, ApprovalTicketPresentation.Classify(Proposal("b", "Deploy")));
        Assert.Equal(ApprovalTicketType.ServerUpgrade, ApprovalTicketPresentation.Classify(Proposal("c", "Rollback")));
        Assert.Equal(ApprovalTicketType.Other, ApprovalTicketPresentation.Classify(Proposal("d", "Migration")));
    }

    [Fact]
    public void Snapshot_CountsAndFiltersByTicketType()
    {
        var snapshot = new ApprovalInboxSnapshot(
        [
            new ApprovalInboxItem(ApprovalTicketType.PublishData, Proposal("a", "MetadataRelease")),
            new ApprovalInboxItem(ApprovalTicketType.ServerUpgrade,
                Proposal("b", "Deploy", DeployOperationLifecycle.Submitted)),
            new ApprovalInboxItem(ApprovalTicketType.PublishData, Proposal("c", "MetadataRelease")),
        ]);

        Assert.Equal(3, snapshot.TotalCount);
        Assert.Equal(2, snapshot.AwaitingApprovalCount); // a + c are AwaitingApproval; b is Submitted
        Assert.Equal([ApprovalTicketType.PublishData, ApprovalTicketType.ServerUpgrade], snapshot.PresentTicketTypes);
        Assert.Equal(2, snapshot.ForTicketType(ApprovalTicketType.PublishData).Count);
        Assert.Single(snapshot.ForTicketType(ApprovalTicketType.ServerUpgrade));
    }

    [Fact]
    public async Task GetInbox_OrdersAwaitingApprovalFirst_AndClassifies()
    {
        var now = DateTimeOffset.UtcNow;
        var client = new ConsoleApprovalInboxClient(
            new EmptyReleaseClient(),
            new InMemoryConsoleDeployApprovalClient(
            [
                Proposal("submitted", "Deploy", DeployOperationLifecycle.Submitted, now),
                Proposal("awaiting", "MetadataRelease", DeployOperationLifecycle.AwaitingApproval, now.AddMinutes(-5)),
            ]));

        var result = await client.GetInboxAsync(["submitted", "awaiting"]);

        Assert.True(result.IsAllowed);
        var items = result.Value!.Items;
        Assert.Equal(2, items.Count);
        // Awaiting-approval sorts ahead of submitted even though it is older.
        Assert.Equal("awaiting", items[0].OperationId);
        Assert.Equal(ApprovalTicketType.PublishData, items[0].TicketType);
        Assert.Equal(ApprovalTicketType.ServerUpgrade, items[1].TicketType);
    }

    [Fact]
    public async Task GetInbox_PassesThroughDeniedDeployControlRead()
    {
        var client = new ConsoleApprovalInboxClient(
            new EmptyReleaseClient(),
            new DeniedDeployApprovalClient(OperateSectionStatus.Unavailable, "No active environment profile is selected."));

        var result = await client.GetInboxAsync([]);

        Assert.False(result.IsAllowed);
        Assert.Equal(OperateSectionStatus.Unavailable, result.Status);
        Assert.Equal("No active environment profile is selected.", result.Message);
    }

    private sealed class DeniedDeployApprovalClient : IConsoleDeployApprovalClient
    {
        private readonly OperateSectionStatus _status;
        private readonly string _message;

        public DeniedDeployApprovalClient(OperateSectionStatus status, string message)
        {
            _status = status;
            _message = message;
        }

        public Task<OperateSectionResult<DeployOperationProposal>> GetOperationAsync(
            string operationId, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<DeployOperationProposal>.Denied(_status, _message));

        public Task<OperateSectionResult<IReadOnlyList<DeployOperationProposal>>> ListPendingAsync(
            IReadOnlyList<string> operationIds, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<IReadOnlyList<DeployOperationProposal>>.Denied(_status, _message));

        public Task<OperateSectionResult<DeployOperationProposal>> SubmitAsync(
            string operationId, string? reason, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<DeployOperationProposal>.Denied(_status, _message));

        public Task<OperateSectionResult<DeployOperationProposal>> RollbackAsync(
            string operationId, string? reason, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<DeployOperationProposal>.Denied(_status, _message));
    }

    private sealed class EmptyReleaseClient : IConsoleGitOpsReleaseClient
    {
        public Task<OperateSectionResult<IReadOnlyList<GitOpsReleaseProposal>>> GetReleaseProposalsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<IReadOnlyList<GitOpsReleaseProposal>>.Allowed(
                (IReadOnlyList<GitOpsReleaseProposal>)[]));

        public Task<OperateSectionResult<GitOpsReleaseProposal>> GetReleaseProposalAsync(
            string releasePackageId, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<GitOpsReleaseProposal>.Denied(
                OperateSectionStatus.Missing, "Release not found."));

        public Task<OperateSectionResult<GitOpsReleaseDetail>> GetReleaseDetailAsync(
            string releasePackageId, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<GitOpsReleaseDetail>.Denied(
                OperateSectionStatus.Missing, "Release not found."));

        public Task<OperateSectionResult<GitOpsCoordinatedRelease>> GetCoordinatedReleaseAsync(
            string releasePackageId, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<GitOpsCoordinatedRelease>.Denied(
                OperateSectionStatus.Missing, "No coordinated release."));
    }
}
