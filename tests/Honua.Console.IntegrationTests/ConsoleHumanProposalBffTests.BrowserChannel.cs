using System.Net;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.IntegrationTests;

public sealed partial class ConsoleHumanProposalBffTests
{
    [Theory]
    [InlineData("browser-cookie")]
    [InlineData("edge-forwarded")]
    [InlineData("agent-token")]
    public async Task BrowserSession_IdentityWithoutCallbackProof_RefusesBeforeUpstream(string authenticationScheme)
    {
        await using var host = await Host.StartAsync();
        host.AuthenticationScheme = authenticationScheme;
        using var caller = new HttpClient { BaseAddress = host.Client.BaseAddress };
        caller.DefaultRequestHeaders.Add("Sec-Fetch-Site", "same-origin");
        caller.DefaultRequestHeaders.Add("Sec-Fetch-Mode", "cors");
        caller.DefaultRequestHeaders.Add("Origin", host.Client.BaseAddress!.GetLeftPart(UriPartial.Authority));

        using var response = await caller.GetAsync("/api/console/proposals/session");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, host.ReviewCalls);
        Assert.Equal(0, host.ApprovalCalls);
    }

    [Fact]
    public async Task BrowserSession_CallbackProof_UsesHostOnlySecureHttpOnlyStrictCookie()
    {
        await using var host = await Host.StartAsync();
        var cookie = Assert.Single(host.BrowserProofCookies, cookie => cookie.StartsWith("__Host-HonuaApproval.", StringComparison.Ordinal));

        Assert.StartsWith("__Host-HonuaApproval.", cookie);
        Assert.Contains("; secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("; httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("; samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("; path=/", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", cookie, StringComparison.OrdinalIgnoreCase);
        using var response = await host.Client.GetAsync("/api/console/proposals/session");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(response.Headers, header => header.Key.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase)
            && header.Value.Any(value => value.Contains("HonuaApproval", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("tampered")]
    [InlineData("expired")]
    [InlineData("other-operator")]
    [InlineData("replacement-session")]
    [InlineData("rotated-session")]
    public async Task BrowserSession_InvalidProof_RefusesWithoutUpstream(string change)
    {
        await using var host = await Host.StartAsync();
        var original = host.Client.DefaultRequestHeaders.GetValues("Cookie").Single();
        switch (change)
        {
            case "tampered":
                host.Client.DefaultRequestHeaders.Remove("Cookie");
                host.Client.DefaultRequestHeaders.Add("Cookie", string.Join("; ", original.Split("; ").Select(cookie =>
                    cookie.StartsWith("__Host-HonuaApproval.", StringComparison.Ordinal) ? cookie + "tampered" : cookie)));
                break;
            case "expired": host.AdvanceClock(TimeSpan.FromMinutes(11)); break;
            case "other-operator": host.OperatorKey = "other-human"; break;
            case "replacement-session": host.ReplacePrivateSession(); break;
            case "rotated-session":
                host.IssueBrowserProof(ConsoleServerAuthority.Serving);
                host.Client.DefaultRequestHeaders.Remove("Cookie");
                host.Client.DefaultRequestHeaders.Add("Cookie", original);
                break;
        }

        using var response = await host.Client.GetAsync("/api/console/proposals/session");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, host.ReviewCalls);
        Assert.Equal(0, host.ApprovalCalls);
    }

    [Fact]
    public async Task BrowserDecision_ProofExpiresAfterReview_RefusesWithoutSendingApproval()
    {
        await using var host = await Host.StartAsync();
        host.AdvanceClock(TimeSpan.FromMinutes(9));
        var review = (await host.OpenReviewAsync()).Value!;
        host.AdvanceClock(TimeSpan.FromMinutes(2));

        var decision = await host.DecideAsync(new("proposal", review.Proposal.Version,
            review.Proposal.PlanHash!, review.EnvironmentReference));

        Assert.Equal(OperateSectionStatus.Forbidden, decision.Status);
        Assert.Contains("Sign in", decision.Message, StringComparison.Ordinal);
        Assert.Equal(0, host.ApprovalCalls);
    }

    [Fact]
    public async Task BrowserStatus_ProofRemovedAfterReview_RefusesWithoutReadingUpstream()
    {
        await using var host = await Host.StartAsync();
        Assert.True((await host.OpenReviewAsync()).IsAllowed);
        host.Client.DefaultRequestHeaders.Remove("Cookie");

        var status = await host.ReadStatusAsync();

        Assert.False(status.IsAllowed);
        Assert.Equal(0, host.ReadCalls);
        Assert.Equal(1, host.ReviewCalls);
    }
}
