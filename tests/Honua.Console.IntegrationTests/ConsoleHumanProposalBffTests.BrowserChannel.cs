using System.Net;

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
}
