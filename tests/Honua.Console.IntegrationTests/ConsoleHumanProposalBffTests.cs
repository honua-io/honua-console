using Honua.Console.Contracts;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;
using Honua.Console.Web.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Honua.Console.IntegrationTests;

public sealed partial class ConsoleHumanProposalBffTests
{
    [Theory]
    [InlineData("profile-tenant", "account-tenant", "account-tenant")]
    [InlineData("profile-tenant", " ", "profile-tenant")]
    [InlineData(" ", " ", null)]
    public async Task BrowserReview_EffectiveAccountTenant_AllowsDecision(
        string profileTenant, string accountTenant, string? reviewTenant)
    {
        await using var host = await Host.StartAsync(profileTenant, accountTenant);
        host.ReviewTenantId = reviewTenant;
        var review = (await host.OpenReviewAsync()).Value!;

        var result = await host.DecideAsync(new ConsoleProposalDecision("proposal", review.Proposal.Version,
            review.Proposal.PlanHash!, review.EnvironmentReference));

        Assert.True(result.IsAllowed);
        Assert.Equal(1, host.ApprovalCalls);
    }

    [Fact]
    public async Task BrowserReview_ProfileTenantDiffersFromEffectiveAccountTenant_RefusesApproval()
    {
        await using var host = await Host.StartAsync("profile-tenant", "account-tenant");
        host.ReviewTenantId = "profile-tenant";

        var result = await host.OpenReviewAsync();

        Assert.False(result.IsAllowed);
        Assert.Equal(0, host.ApprovalCalls);
    }

    [Theory]
    [InlineData("tenant-one", "tenant-two")]
    [InlineData("tenant-one", null)]
    [InlineData("", "tenant-one")]
    public async Task BrowserReview_DifferentTenant_RefusesApproval(string selectedTenant, string? reviewTenant)
    {
        await using var host = await Host.StartAsync(selectedTenant);
        host.ReviewTenantId = reviewTenant;

        var result = await host.OpenReviewAsync();

        Assert.False(result.IsAllowed);
        Assert.Equal(0, host.ApprovalCalls);
    }

    [Theory]
    [InlineData("")]
    [InlineData("tenant-one")]
    public async Task BrowserReview_MatchingTenant_AllowsDecision(string tenant)
    {
        await using var host = await Host.StartAsync(tenant);
        var review = (await host.OpenReviewAsync()).Value!;

        var result = await host.DecideAsync(new ConsoleProposalDecision("proposal", review.Proposal.Version,
            review.Proposal.PlanHash!, review.EnvironmentReference));

        Assert.True(result.IsAllowed);
        Assert.Equal(1, host.ApprovalCalls);
    }

    [Fact]
    public async Task BrowserReview_MissingTenant_RefusesApproval()
    {
        await using var host = await Host.StartAsync();
        host.OmitReviewTenant = true;

        var result = await host.OpenReviewAsync();

        Assert.False(result.IsAllowed);
        Assert.Equal(0, host.ApprovalCalls);
    }

    [Fact]
    public async Task BrowserReview_TenantMismatchAfterValidReview_DiscardsPreviousApproval()
    {
        await using var host = await Host.StartAsync("tenant-one");
        var review = (await host.OpenReviewAsync()).Value!;
        host.ReviewTenantId = "tenant-two";

        var refreshed = await host.OpenReviewAsync();
        var result = await host.DecideAsync(new ConsoleProposalDecision("proposal", review.Proposal.Version,
            review.Proposal.PlanHash!, review.EnvironmentReference));

        Assert.False(refreshed.IsAllowed);
        Assert.False(result.IsAllowed);
        Assert.Equal(0, host.ApprovalCalls);
    }

    [Fact]
    public async Task BrowserReviewAndDecision_KeepUpstreamCredentialsPrivateAndConsumeOnce()
    {
        await using var host = await Host.StartAsync();
        var review = await host.OpenReviewAsync();
        Assert.True(review.IsAllowed);
        var value = review.Value!;
        Assert.DoesNotContain("private-capability", JsonSerializer.Serialize(review));
        Assert.DoesNotContain("private-server-session", JsonSerializer.Serialize(review));

        var decision = new ConsoleProposalDecision("proposal", value.Proposal.Version,
            value.Proposal.PlanHash!, value.EnvironmentReference);
        var approved = await host.DecideAsync(decision);
        var duplicate = await host.DecideAsync(decision);

        Assert.True(approved.IsAllowed);
        Assert.False(duplicate.IsAllowed);
        Assert.Equal(1, host.ApprovalCalls);
        Assert.True(host.SawPrivateCookie);
        Assert.False(host.SawForwardableCredential);
    }

    [Theory]
    [InlineData("csrf")]
    [InlineData("bearer")]
    [InlineData("key")]
    [InlineData("anonymous")]
    [InlineData("profile")]
    public async Task BrowserReview_UntrustedChannel_RefusesBeforeUpstream(string change)
    {
        await using var host = await Host.StartAsync();
        switch (change)
        {
            case "csrf": host.Client.DefaultRequestHeaders.Remove("X-CSRF-Token"); break;
            case "bearer": host.Client.DefaultRequestHeaders.Add("Authorization", "Bearer noninteractive"); break;
            case "key": host.Client.DefaultRequestHeaders.Add("X-API-Key", "noninteractive"); break;
            case "anonymous": host.Authenticated = false; break;
        }
        using var response = await host.Client.PostAsJsonAsync("/api/console/proposals/proposal/review",
            new ConsoleHumanProposalRequest(change == "profile" ? "other" : "environment", ConsoleServerAuthority.Serving, null),
            ConsoleHumanProposalBffJsonContext.Default.ConsoleHumanProposalRequest);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, host.ReviewCalls);
        Assert.Equal(0, host.ApprovalCalls);
    }

    [Theory]
    [InlineData("version")]
    [InlineData("plan")]
    [InlineData("environment")]
    [InlineData("logout")]
    public async Task BrowserDecision_ReviewAuthorityChanged_RefusesBeforeExecution(string change)
    {
        await using var host = await Host.StartAsync();
        var review = (await host.OpenReviewAsync()).Value!;
        var decision = new ConsoleProposalDecision("proposal", review.Proposal.Version,
            review.Proposal.PlanHash!, review.EnvironmentReference);
        decision = change switch
        {
            "version" => decision with { ProposalVersion = decision.ProposalVersion + 1 },
            "plan" => decision with { PlanHash = "changed-plan" },
            "environment" => decision with { EnvironmentReference = "other-environment" },
            _ => decision
        };
        if (change == "logout") host.Sessions.Remove("human", host.Profile);

        var result = await host.DecideAsync(decision);

        Assert.False(result.IsAllowed);
        Assert.Equal(0, host.ApprovalCalls);
    }

    [Fact]
    public async Task BrowserDecision_LostResponse_DoesNotResubmitConsumedCapability()
    {
        await using var host = await Host.StartAsync();
        var review = (await host.OpenReviewAsync()).Value!;
        var decision = new ConsoleProposalDecision("proposal", review.Proposal.Version,
            review.Proposal.PlanHash!, review.EnvironmentReference);
        host.LoseDecisionResponse = true;

        var uncertain = await host.DecideAsync(decision);
        var retry = await host.DecideAsync(decision);

        Assert.Equal(OperateSectionStatus.Unavailable, uncertain.Status);
        Assert.False(retry.IsAllowed);
        Assert.Equal(1, host.ApprovalCalls);
    }

    [Fact]
    public async Task BrowserRead_ResolvedProposal_UsesPrivateSessionAndDiscardsOldApproval()
    {
        await using var host = await Host.StartAsync();
        var pending = (await host.OpenReviewAsync()).Value!;
        var decision = new ConsoleProposalDecision("proposal", pending.Proposal.Version,
            pending.Proposal.PlanHash!, pending.EnvironmentReference);
        host.ProposalStatus = "Executing";

        var resolved = await host.OpenReviewAsync();
        var oldApproval = await host.DecideAsync(decision);

        Assert.True(resolved.IsAllowed);
        Assert.Equal("Executing", resolved.Value!.Proposal.Status);
        Assert.Empty(resolved.Value.EnvironmentReference);
        Assert.False(oldApproval.IsAllowed);
        Assert.Equal(0, host.ApprovalCalls);
        Assert.True(host.SawPrivateCookie);
    }

    [Fact]
    public async Task BrowserDecision_EnvironmentChangesDuringIdentityCheck_RefusesBeforeSubmission()
    {
        await using var host = await Host.StartAsync();
        var review = (await host.OpenReviewAsync()).Value!;
        var decision = new ConsoleProposalDecision("proposal", review.Proposal.Version,
            review.Proposal.PlanHash!, review.EnvironmentReference);
        host.SwitchDuringIdentityCheck = true;

        var result = await host.DecideAsync(decision);

        Assert.False(result.IsAllowed);
        Assert.Equal(0, host.ApprovalCalls);
    }

}
