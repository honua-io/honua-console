using System.Net;
using Honua.Console.Shell.Models;

namespace Honua.Console.IntegrationTests;

public sealed partial class ConsoleHumanProposalBffTests
{
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
