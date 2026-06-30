using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.Native.Core.Tests;

/// <summary>
/// Pins the approval-inbox (#193) projection over the first-class honua-server proposals API
/// (#1694): the GIS-desk ticket-type classification (MetadataRelease/Seed → publish data,
/// DataImport → import, Deploy → server upgrade, AdminConfigChange → access/config), the
/// snapshot helpers (counts, present types, filter), the awaiting-approval-first ordering, and
/// the aggregator's honest missing-binding pass-through. The aggregator never fabricates a
/// queue; a denied proposals read surfaces as a denied inbox read (charter §11).
/// </summary>
public sealed class ApprovalInboxTests
{
    private static ConsoleProposalSummary Proposal(
        string id,
        ConsoleProposalKind kind,
        ConsoleProposalStatus status = ConsoleProposalStatus.AwaitingApproval,
        DateTimeOffset? updatedAt = null) => new(
        ProposalId: id,
        Kind: kind,
        Status: status,
        RequestedBy: "agent",
        RequestedByAgent: "agent",
        Summary: "summary",
        RiskLevel: ConsoleProposalRisk.Low,
        CreatedAt: DateTimeOffset.UtcNow,
        UpdatedAt: updatedAt ?? DateTimeOffset.UtcNow);

    [Fact]
    public void Classify_MapsKindsOntoTicketTypes()
    {
        Assert.Equal(ApprovalTicketType.PublishData, ApprovalTicketPresentation.Classify(ConsoleProposalKind.MetadataRelease));
        Assert.Equal(ApprovalTicketType.PublishData, ApprovalTicketPresentation.Classify(ConsoleProposalKind.Seed));
        Assert.Equal(ApprovalTicketType.DataImport, ApprovalTicketPresentation.Classify(ConsoleProposalKind.DataImport));
        Assert.Equal(ApprovalTicketType.ServerUpgrade, ApprovalTicketPresentation.Classify(ConsoleProposalKind.Deploy));
        Assert.Equal(ApprovalTicketType.AccessConfig, ApprovalTicketPresentation.Classify(ConsoleProposalKind.AdminConfigChange));
        Assert.Equal(ApprovalTicketType.Other, ApprovalTicketPresentation.Classify(ConsoleProposalKind.Unknown));
    }

    [Theory]
    [InlineData("DataImport", ConsoleProposalKind.DataImport)]
    [InlineData("ImportDataset", ConsoleProposalKind.DataImport)]
    [InlineData("import", ConsoleProposalKind.DataImport)]
    [InlineData("metadata-release", ConsoleProposalKind.MetadataRelease)]
    [InlineData("ADMINCONFIGCHANGE", ConsoleProposalKind.AdminConfigChange)]
    [InlineData("nonsense", ConsoleProposalKind.Unknown)]
    public void MapKind_ParsesWireStringsCaseAndShapeInsensitively(string raw, ConsoleProposalKind expected)
    {
        Assert.Equal(expected, ConsoleProposalPresentation.MapKind(raw));
    }

    [Fact]
    public void Snapshot_CountsAndFiltersByTicketType()
    {
        var snapshot = new ApprovalInboxSnapshot(
        [
            new ApprovalInboxItem(ApprovalTicketType.PublishData, Proposal("a", ConsoleProposalKind.MetadataRelease)),
            new ApprovalInboxItem(ApprovalTicketType.ServerUpgrade,
                Proposal("b", ConsoleProposalKind.Deploy, ConsoleProposalStatus.Submitted)),
            new ApprovalInboxItem(ApprovalTicketType.PublishData, Proposal("c", ConsoleProposalKind.MetadataRelease)),
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
        var client = new ConsoleApprovalInboxClient(new StubProposalsClient(
        [
            Proposal("submitted", ConsoleProposalKind.Deploy, ConsoleProposalStatus.Submitted, now),
            Proposal("awaiting", ConsoleProposalKind.MetadataRelease, ConsoleProposalStatus.AwaitingApproval, now.AddMinutes(-5)),
        ]));

        var result = await client.GetInboxAsync();

        Assert.True(result.IsAllowed);
        var items = result.Value!.Items;
        Assert.Equal(2, items.Count);
        // Awaiting-approval sorts ahead of submitted even though it is older.
        Assert.Equal("awaiting", items[0].ProposalId);
        Assert.Equal(ApprovalTicketType.PublishData, items[0].TicketType);
        Assert.Equal(ApprovalTicketType.ServerUpgrade, items[1].TicketType);
    }

    [Fact]
    public async Task GetInbox_PassesThroughDeniedProposalsRead()
    {
        var client = new ConsoleApprovalInboxClient(
            new StubProposalsClient(OperateSectionStatus.Forbidden, "Approving requires the 'approve' permission."));

        var result = await client.GetInboxAsync();

        Assert.False(result.IsAllowed);
        Assert.Equal(OperateSectionStatus.Forbidden, result.Status);
        Assert.Equal("Approving requires the 'approve' permission.", result.Message);
    }

    private sealed class StubProposalsClient : IConsoleProposalsClient
    {
        private readonly OperateSectionResult<IReadOnlyList<ConsoleProposalSummary>> _list;

        public StubProposalsClient(IReadOnlyList<ConsoleProposalSummary> proposals) =>
            _list = OperateSectionResult<IReadOnlyList<ConsoleProposalSummary>>.Allowed(proposals);

        public StubProposalsClient(OperateSectionStatus status, string message) =>
            _list = OperateSectionResult<IReadOnlyList<ConsoleProposalSummary>>.Denied(status, message);

        public Task<OperateSectionResult<IReadOnlyList<ConsoleProposalSummary>>> ListAsync(
            string? status = null, string? kind = null, string? requestedBy = null,
            CancellationToken cancellationToken = default) => Task.FromResult(_list);

        public Task<OperateSectionResult<ConsoleProposalDetail>> GetAsync(
            string proposalId, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<ConsoleProposalDetail>.Denied(OperateSectionStatus.Missing, "not used"));

        public Task<OperateSectionResult<ConsoleProposalDetail>> ApproveAsync(
            string proposalId, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<ConsoleProposalDetail>.Denied(OperateSectionStatus.Missing, "not used"));

        public Task<OperateSectionResult<ConsoleProposalDetail>> RejectAsync(
            string proposalId, string reason, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<ConsoleProposalDetail>.Denied(OperateSectionStatus.Missing, "not used"));
    }
}
