using System.Net;
using System.Net.Http.Json;

namespace Honua.Console.IntegrationTests;

public sealed partial class ConsoleHumanProposalBffTests
{
    [Fact]
    public async Task StudioDraft_VerifiedBrowser_UsesPrivateUpstreamSessionWithoutBearer()
    {
        await using var host = await Host.StartAsync();

        using var response = await host.Client.PostAsJsonAsync("/api/studio/v1/studio/package-drafts", new { });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(1, host.DraftCalls);
        Assert.True(host.SawPrivateCookie);
        Assert.False(host.SawForwardableCredential);
    }

    [Theory]
    [InlineData("missing-proof")]
    [InlineData("expired-proof")]
    [InlineData("replacement-session")]
    [InlineData("bearer")]
    [InlineData("key")]
    public async Task StudioDraft_UnverifiedBrowserChannel_DoesNotDispatchAsHuman(string change)
    {
        await using var host = await Host.StartAsync();
        switch (change)
        {
            case "missing-proof": host.Client.DefaultRequestHeaders.Remove("Cookie"); break;
            case "expired-proof": host.AdvanceClock(TimeSpan.FromMinutes(11)); break;
            case "replacement-session": host.ReplacePrivateSession(); break;
            case "bearer": host.Client.DefaultRequestHeaders.Add("Authorization", "Bearer agent-token"); break;
            case "key": host.Client.DefaultRequestHeaders.Add("X-API-Key", "agent-key"); break;
        }

        using var response = await host.Client.PostAsJsonAsync("/api/studio/v1/studio/package-drafts", new { });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, host.DraftCalls);
    }
}
