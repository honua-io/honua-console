using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;
using Xunit;

namespace Honua.Console.Native.Core.Tests;

public sealed class DualConsoleProposalsClientTests
{
    [Fact]
    public async Task ListsBothUpstreamsAndRoutesDecisionsToTheirOwner()
    {
        var serving = new FakeProposals("serving-proposal");
        var manager = new FakeProposals("manager-proposal");
        var profiles = new InMemoryConsoleEnvironmentProfileStore(
            [new ConsoleEnvironmentProfile { Id = "local", ServerBaseUri = new Uri("https://serving.example/") }], activeProfileId: "local");
        var client = new DualConsoleProposalsClient(serving, manager, profiles, new ManagerEndpoint());
        var list = await client.ListAsync();
        Assert.True(list.IsAllowed);
        Assert.Contains(list.Value!, item => item.ProposalId == "serving-proposal");
        Assert.Contains(list.Value!, item => item.ProposalId == "manager:manager-proposal");
        await client.ApproveAsync("serving-proposal");
        await client.ApproveAsync("manager:manager-proposal");
        Assert.Equal("serving-proposal", serving.Approved);
        Assert.Equal("manager-proposal", manager.Approved);
    }

    [Fact]
    public async Task ManagerProposalsRemainVisibleWhenServingIsUnavailable()
    {
        var serving = new FakeProposals("serving-proposal") { Unavailable = true };
        var manager = new FakeProposals("manager-proposal");
        var profiles = new InMemoryConsoleEnvironmentProfileStore(
            [new ConsoleEnvironmentProfile { Id = "local", ServerBaseUri = new Uri("https://serving.example/") }], activeProfileId: "local");
        var client = new DualConsoleProposalsClient(serving, manager, profiles, new ManagerEndpoint());
        var list = await client.ListAsync();
        Assert.True(list.IsAllowed);
        Assert.True(list.PartialResult);
        Assert.Contains(list.Value!, item => item.ProposalId == "manager:manager-proposal");
    }

    [Fact]
    public async Task SlowServingSourceDoesNotHideManagerRecoveryQueue()
    {
        var serving = new FakeProposals("serving-proposal") { Hang = true };
        var manager = new FakeProposals("manager-proposal");
        var profiles = new InMemoryConsoleEnvironmentProfileStore(
            [new ConsoleEnvironmentProfile { Id = "local", ServerBaseUri = new Uri("https://serving.example/") }], activeProfileId: "local");
        var client = new DualConsoleProposalsClient(serving, manager, profiles, new ManagerEndpoint());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        var list = await client.ListAsync(cancellationToken: timeout.Token);
        Assert.True(list.IsAllowed);
        Assert.True(list.PartialResult);
        Assert.Contains(list.Value!, item => item.ProposalId == "manager:manager-proposal");
    }

    private sealed class ManagerEndpoint : IConsoleManagerEndpoint
    {
        public Uri? Resolve(Uri servingBaseUri) => new("https://manager.example/");
    }

    private sealed class FakeProposals(string id) : IConsoleProposalsClient
    {
        public string? Approved { get; private set; }
        public bool Unavailable { get; init; }
        public bool Hang { get; init; }
        public Task<OperateSectionResult<IReadOnlyList<ConsoleProposalSummary>>> ListAsync(
            string? status = null, string? kind = null, string? requestedBy = null, CancellationToken cancellationToken = default) =>
            Hang ? Task.Delay(Timeout.Infinite, cancellationToken).ContinueWith<OperateSectionResult<IReadOnlyList<ConsoleProposalSummary>>>(
                _ => throw new OperationCanceledException(cancellationToken), cancellationToken) : Task.FromResult(Unavailable
                ? OperateSectionResult<IReadOnlyList<ConsoleProposalSummary>>.Denied(OperateSectionStatus.Unavailable, "offline")
                : OperateSectionResult<IReadOnlyList<ConsoleProposalSummary>>.Allowed(
                    [new ConsoleProposalSummary(id, default, default, null, null, "", default, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)]));
        public Task<OperateSectionResult<ConsoleProposalDetail>> GetAsync(string proposalId, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<ConsoleProposalDetail>.Denied(OperateSectionStatus.Missing, "Not found"));
        public Task<OperateSectionResult<ConsoleProposalDetail>> ApproveAsync(string proposalId, CancellationToken cancellationToken = default)
        {
            Approved = proposalId;
            return GetAsync(proposalId, cancellationToken);
        }
        public Task<OperateSectionResult<ConsoleProposalDetail>> RejectAsync(string proposalId, string reason, CancellationToken cancellationToken = default) =>
            GetAsync(proposalId, cancellationToken);
    }
}
