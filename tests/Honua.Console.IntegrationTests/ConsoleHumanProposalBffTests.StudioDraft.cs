using System.Net;
using System.Net.Http.Json;
using Honua.Console.Shell.Models;

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

    [Fact]
    public async Task StudioDraft_ApprovalProofExpired_EditContinuesWithoutRenewingApproval()
    {
        await using var host = await Host.StartAsync();
        host.AdvanceClock(TimeSpan.FromMinutes(11));

        using var edit = await host.Client.PostAsJsonAsync("/api/studio/v1/studio/package-drafts", new { });
        using var approval = await host.Client.GetAsync("/api/console/proposals/session");

        Assert.Equal(HttpStatusCode.Created, edit.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, approval.StatusCode);
        Assert.DoesNotContain(edit.Headers, header => header.Key.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase)
            && header.Value.Any(value => value.Contains("HonuaApproval", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task StudioDraft_ApprovalProofAlone_DoesNotAuthorizeEditing()
    {
        await using var host = await Host.StartAsync();
        var approvalCookies = host.Client.DefaultRequestHeaders.GetValues("Cookie").Single().Split("; ")
            .Where(cookie => cookie.StartsWith("__Host-HonuaApproval.", StringComparison.Ordinal));
        host.Client.DefaultRequestHeaders.Remove("Cookie");
        host.Client.DefaultRequestHeaders.Add("Cookie", string.Join("; ", approvalCookies));

        using var response = await host.Client.PostAsJsonAsync("/api/studio/v1/studio/package-drafts", new { });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, host.DraftCalls);
    }

    [Theory]
    [InlineData("missing-proof")]
    [InlineData("expired-proof")]
    [InlineData("replacement-session")]
    [InlineData("bearer")]
    [InlineData("key")]
    [InlineData("other-operator")]
    [InlineData("other-origin")]
    [InlineData("tampered")]
    [InlineData("rotated-proof")]
    [InlineData("rotated-upstream-cookie")]
    [InlineData("shortened-session")]
    public async Task StudioDraft_UnverifiedBrowserChannel_DoesNotDispatchAsHuman(string change)
    {
        await using var host = await Host.StartAsync();
        switch (change)
        {
            case "missing-proof": host.Client.DefaultRequestHeaders.Remove("Cookie"); break;
            case "expired-proof": host.AdvanceClock(TimeSpan.FromMinutes(61)); break;
            case "replacement-session": host.ReplacePrivateSession(); break;
            case "bearer": host.Client.DefaultRequestHeaders.Add("Authorization", "Bearer agent-token"); break;
            case "key": host.Client.DefaultRequestHeaders.Add("X-API-Key", "agent-key"); break;
            case "other-operator": host.OperatorKey = "other-human"; break;
            case "other-origin": host.Client.DefaultRequestHeaders.Host = "other-console.example"; break;
            case "tampered":
                var cookies = host.Client.DefaultRequestHeaders.GetValues("Cookie").Single();
                host.Client.DefaultRequestHeaders.Remove("Cookie");
                host.Client.DefaultRequestHeaders.Add("Cookie", cookies + "tampered");
                break;
            case "rotated-proof":
                var original = host.Client.DefaultRequestHeaders.GetValues("Cookie").Single();
                host.IssueBrowserProof(ConsoleServerAuthority.Serving);
                host.Client.DefaultRequestHeaders.Remove("Cookie");
                host.Client.DefaultRequestHeaders.Add("Cookie", original);
                break;
            case "rotated-upstream-cookie": host.RotateUpstreamSessionCookie(); break;
            case "shortened-session": host.SessionExpiresAt = host.Clock.GetUtcNow().AddMinutes(1); break;
        }

        using var response = await host.Client.PostAsJsonAsync("/api/studio/v1/studio/package-drafts", new { });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, host.DraftCalls);
    }
}
