using System.Net;
using System.Security.Claims;
using System.Text;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;
using Honua.Console.Web.Runtime;

namespace Honua.Console.IntegrationTests;

// The static Console approves over its same-origin cookie session with the review capability.
public sealed class BrowserProposalApprovalTests
{
    private static readonly Uri Api = new("https://honua.example/");
    private static readonly Uri App = new("https://honua.example/console/");

    [Fact]
    public async Task SignedInCookieSessionApprovesWithCapabilityAndCsrfButNoExplicitCredential()
    {
        var session = new ConsoleBrowserSession();
        session.Set(Principal(), new ConsoleSessionCredentials(CsrfToken: "issued-csrf"));
        var server = new ApprovalServer();
        var client = Proposals(session, server);

        var result = await client.ApproveAsync("prop-1", new ConsoleProposalReviewedPlan(3, "plan-3"));

        Assert.Equal(OperateSectionStatus.Allowed, result.Status);
        Assert.Equal(ConsoleProposalStatus.Submitted, result.Value!.Status);
        Assert.Collection(
            server.Requests,
            review =>
            {
                Assert.Equal("GET /api/v1/admin/proposals/prop-1/approval-review", review.Line);
                Assert.Null(review.Authorization);
                Assert.Null(review.ApiKey);
            },
            approve =>
            {
                Assert.Equal("POST /api/v1/admin/proposals/prop-1/approve", approve.Line);
                Assert.Equal("capability-3", approve.Capability);
                Assert.Equal("issued-csrf", approve.Csrf);
                Assert.Null(approve.Authorization);
                Assert.Null(approve.ApiKey);
            });
    }

    [Fact]
    public async Task SameOriginApproveWithoutAnIssuedCsrfTokenFailsBeforeTheDecisionIsSent()
    {
        var session = new ConsoleBrowserSession();
        session.Set(Principal());
        var server = new ApprovalServer();
        var client = Proposals(session, server);

        var result = await client.ApproveAsync("prop-1", new ConsoleProposalReviewedPlan(3, "plan-3"));

        Assert.False(result.IsAllowed);
        var review = Assert.Single(server.Requests);
        Assert.Equal("GET /api/v1/admin/proposals/prop-1/approval-review", review.Line);
    }

    [Fact]
    public async Task RejectIsNotBlockedByTheShellBearerGateUnderACookieSession()
    {
        var session = new ConsoleBrowserSession();
        session.Set(Principal(), new ConsoleSessionCredentials(CsrfToken: "issued-csrf"));
        var server = new ApprovalServer();
        var client = Proposals(session, server);

        var result = await client.RejectAsync("prop-1", "Out of window");

        Assert.Equal(OperateSectionStatus.Allowed, result.Status);
        var reject = Assert.Single(server.Requests);
        Assert.Equal("POST /api/v1/admin/proposals/prop-1/reject", reject.Line);
        Assert.Equal("issued-csrf", reject.Csrf);
        Assert.Null(reject.Authorization);
    }

    [Fact]
    public async Task BearerProviderReportsBrowserTransportOnlyForASignedInSessionOnTheConfiguredApi()
    {
        var session = new ConsoleBrowserSession();
        var provider = new BrowserOperatorBearerProvider(session, Api);
        var configured = new ConsoleEnvironmentProfile { Id = "configured-api", ServerBaseUri = Api };

        var signedOut = await provider.ResolveAsync(configured);
        Assert.False(signedOut.UsesBrowserTransport);
        Assert.Contains("Sign in", signedOut.Message, StringComparison.Ordinal);

        session.Set(Principal());
        var signedIn = await provider.ResolveAsync(configured);
        Assert.True(signedIn.UsesBrowserTransport);
        Assert.True(signedIn.HasInteractiveSession);
        Assert.False(signedIn.IsAvailable);

        var other = await provider.ResolveAsync(new ConsoleEnvironmentProfile { Id = "other", ServerBaseUri = new Uri("https://other.example/") });
        Assert.False(other.UsesBrowserTransport);
    }

    private static HttpConsoleProposalsClient Proposals(ConsoleBrowserSession session, ApprovalServer server)
    {
        var http = new HttpClient(new BrowserApiHandler(new BrowserApiOrigins(Api, App), session, BrowserApiAccess.Authenticated)
        {
            InnerHandler = server
        })
        { BaseAddress = Api };
        var profiles = new InMemoryConsoleEnvironmentProfileStore(
            [new ConsoleEnvironmentProfile { Id = "configured-api", ServerBaseUri = Api }], activeProfileId: "configured-api");
        return new HttpConsoleProposalsClient(http, profiles, new InMemoryConsoleAccountSessionStore(),
            adminApiKey: null, operatorBearerProvider: new BrowserOperatorBearerProvider(session, Api));
    }

    private static ClaimsPrincipal Principal() =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "operator")], "test"));

    private sealed record SeenRequest(string Line, string? Authorization, string? ApiKey, string? Csrf, string? Capability);

    private sealed class ApprovalServer : HttpMessageHandler
    {
        private const string Detail = """
            { "proposalId": "prop-1", "version": 3, "planHash": "plan-3", "kind": "Deploy", "status": "Submitted",
              "summary": "Upgrade", "createdAt": "2026-06-28T10:00:00Z", "updatedAt": "2026-06-28T10:10:00Z" }
            """;

        private const string Review = """
            { "proposal": { "proposalId": "prop-1", "version": 3, "kind": "Deploy", "status": "AwaitingApproval",
                "summary": "Upgrade", "createdAt": "2026-06-28T10:00:00Z", "updatedAt": "2026-06-28T10:05:00Z" },
              "review": { "tenantId": null, "proposalId": "prop-1", "proposalVersion": 3, "planHash": "plan-3",
                "expiresAt": "2026-06-28T10:15:00Z", "capability": "capability-3" } }
            """;

        public List<SeenRequest> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new SeenRequest(
                $"{request.Method.Method} {request.RequestUri!.AbsolutePath}",
                request.Headers.Authorization?.ToString(),
                Header(request, "X-API-Key"),
                Header(request, "X-Honua-CSRF"),
                Header(request, "X-Honua-Approval-Capability")));
            var body = request.RequestUri.AbsolutePath.EndsWith("/approval-review", StringComparison.Ordinal) ? Review : Detail;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }

        private static string? Header(HttpRequestMessage request, string name) =>
            request.Headers.TryGetValues(name, out var values) ? values.Single() : null;
    }
}
