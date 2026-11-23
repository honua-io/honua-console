using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Tests;

public sealed class ApprovalInboxPresentationTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);

    private static ConsoleProposalSummary Proposal(string? person, string? agent) => new(
        ProposalId: "p-1",
        Kind: ConsoleProposalKind.Deploy,
        Status: ConsoleProposalStatus.AwaitingApproval,
        RequestedBy: person,
        RequestedByAgent: agent,
        Summary: "summary",
        RiskLevel: ConsoleProposalRisk.Low,
        CreatedAt: Now,
        UpdatedAt: Now);

    [Theory]
    [InlineData("kai@example.org", "Claude Desktop", "Claude Desktop | Agent · on behalf of kai@example.org")]
    [InlineData("agent.ingest", "agent.ingest", "agent.ingest | Agent")]
    [InlineData(" ", "catalog-curator", "catalog-curator | Agent · person not reported")]
    [InlineData("kai@example.org", null, "kai@example.org | Person")]
    [InlineData(null, "", "Not reported | Requester not reported")]
    public void Requester_NamesTheAgentAndThePersonItActsFor(string? person, string? agent, string expected)
    {
        var requester = ApprovalInboxPresentation.Requester(Proposal(person, agent));

        Assert.Equal(expected, $"{requester.Name} | {requester.Channel}");
    }

    [Theory]
    [InlineData(30, "under 1 min")]
    [InlineData(2 * 60, "2 min")]
    [InlineData(59 * 60 + 59, "59 min")]
    [InlineData(60 * 60, "1 h")]
    [InlineData(25 * 60 * 60, "1 d")]
    public void WaitingFor_RoundsDownToTheLargestWholeUnit(int seconds, string expected)
    {
        Assert.Equal(expected, ApprovalInboxPresentation.WaitingFor(Now.AddSeconds(-seconds), Now));
    }

    [Fact]
    public void IsReportedTime_RejectsServerPlaceholders()
    {
        Assert.False(ApprovalInboxPresentation.IsReportedTime(DateTimeOffset.UnixEpoch));
        Assert.False(ApprovalInboxPresentation.IsReportedTime(DateTimeOffset.MinValue));
        Assert.True(ApprovalInboxPresentation.IsReportedTime(Now));
    }

    [Theory]
    [InlineData(0, "No proposals waiting.")]
    [InlineData(1, "1 proposal waiting.")]
    [InlineData(5, "5 proposals waiting.")]
    public void WaitingSentence_CountsAwaitingProposals(int awaiting, string expected)
    {
        Assert.Equal(expected, ApprovalInboxPresentation.WaitingSentence(awaiting));
    }

    [Theory]
    [InlineData(0, 3, "No results")]
    [InlineData(2, 3, "Results 1 to 2 (out of 3)")]
    public void Results_DescribesTheVisibleRange(int shown, int total, string expected)
    {
        Assert.Equal(expected, ApprovalInboxPresentation.Results(shown, total));
    }
}
