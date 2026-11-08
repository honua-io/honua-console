using System.Net;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.IntegrationTests;

public sealed partial class ConsoleHumanProposalBffTests
{
    [Fact]
    public async Task BrowserReview_ManagementNotSeparate_RefusesBeforeUsingTheServingSession()
    {
        await using var host = await Host.StartAsync();

        var result = await host.OpenReviewAsync(ConsoleServerAuthority.Management);

        Assert.False(result.IsAllowed);
        Assert.Equal(0, host.ReviewCalls);
        Assert.Equal(0, host.ApprovalCalls);
    }

    [Fact]
    public async Task BrowserReview_SameOriginManagementPath_KeepsBothAuthoritiesAndCapabilitiesSeparate()
    {
        await using var host = await Host.StartAsync();
        host.ConfigureManagement();
        var serving = (await host.OpenReviewAsync()).Value!;
        var manager = (await host.OpenReviewAsync(ConsoleServerAuthority.Management)).Value!;

        var managementDecision = await host.DecideAsync(new("proposal", manager.Proposal.Version,
            manager.Proposal.PlanHash!, manager.EnvironmentReference), ConsoleServerAuthority.Management);
        var servingDecision = await host.DecideAsync(new("proposal", serving.Proposal.Version,
            serving.Proposal.PlanHash!, serving.EnvironmentReference));

        Assert.NotEqual(serving.EnvironmentReference, manager.EnvironmentReference);
        Assert.True(managementDecision.IsAllowed);
        Assert.True(servingDecision.IsAllowed);
        Assert.Equal(2, host.ApprovalCalls);
        Assert.False(host.SawForwardableCredential);
    }

    [Fact]
    public async Task BrowserStatus_PendingProposal_DiscardsOldCapabilityWithoutOpeningAReview()
    {
        await using var host = await Host.StartAsync();
        var review = (await host.OpenReviewAsync()).Value!;

        var result = await host.ReadStatusAsync();
        var oldDecision = await host.DecideAsync(new("proposal", review.Proposal.Version,
            review.Proposal.PlanHash!, review.EnvironmentReference));

        Assert.True(result.IsAllowed);
        Assert.Equal("AwaitingApproval", result.Value!.Status);
        Assert.Equal(1, host.ReviewCalls);
        Assert.Equal(1, host.ReadCalls);
        Assert.Null(host.TakeCachedExpiry());
        Assert.False(oldDecision.IsAllowed);
        Assert.Equal(0, host.ApprovalCalls);
    }

    [Fact]
    public async Task BrowserReview_ServerClockOneSecondAhead_AllowsReviewWithBoundedLocalExpiry()
    {
        await using var host = await Host.StartAsync();
        host.ReviewClockSkew = TimeSpan.FromSeconds(1);

        var result = await host.OpenReviewAsync();

        Assert.True(result.IsAllowed);
        Assert.Equal(host.Clock.GetUtcNow().AddMinutes(5), result.Value!.ExpiresAt);
        Assert.Equal(result.Value.ExpiresAt, host.TakeCachedExpiry());
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("long-lifetime")]
    [InlineData("missing-no-store")]
    [InlineData("oversized-capability")]
    [InlineData("resolved-status")]
    [InlineData("version")]
    [InlineData("plan")]
    [InlineData("proposal-id")]
    public async Task BrowserReview_UnverifiedEnvelope_RefusesWithoutRetainingCapability(string mutation)
    {
        await using var host = await Host.StartAsync();
        host.ReviewMutation = mutation;

        var result = await host.OpenReviewAsync();

        Assert.Equal(OperateSectionStatus.Unavailable, result.Status);
        Assert.Null(host.TakeCachedExpiry());
        Assert.Equal(0, host.ApprovalCalls);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.NotImplemented)]
    public async Task BrowserReview_OldServer_ReportsUnsupportedWithoutOpeningAnotherRead(HttpStatusCode status)
    {
        await using var host = await Host.StartAsync();
        host.ReviewStatusCode = status;

        var result = await host.OpenReviewAsync();

        Assert.Equal(OperateSectionStatus.Unsupported, result.Status);
        Assert.Contains("does not support", result.Message, StringComparison.Ordinal);
        Assert.Equal(0, host.ReadCalls);
        Assert.Null(host.TakeCachedExpiry());
    }

    [Fact]
    public async Task BrowserRead_ResolvedProposalUnderPathBase_ReadsOriginalFullAuthority()
    {
        await using var host = await Host.StartAsync(pathBase: "/installed");
        host.ProposalStatus = "Executing";

        var result = await host.OpenReviewAsync();

        Assert.True(result.IsAllowed);
        Assert.Equal("Executing", result.Value!.Proposal.Status);
        Assert.Equal(1, host.ReadCalls);
        Assert.Null(host.TakeCachedExpiry());
    }
}
