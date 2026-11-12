using System.Net;
using System.Text;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.Shell.Tests;

// Approval sends only the review capability over the ambient human session; other decisions use the operator bearer.
public sealed class ConsoleProposalsClientTests
{
    [Theory]
    [InlineData("Completed", "completed", true)]
    [InlineData("Cancelled", "cancelled", true)]
    [InlineData("Indeterminate", "indeterminate", true)]
    [InlineData("Executing", "executing", false)]
    public async Task DetailMapsActualExecutorStates(string wireStatus, string label, bool terminal)
    {
        var body = $$"""{"proposalId":"prop-1","kind":"Deploy","status":"{{wireStatus}}","createdAt":"2026-06-28T10:00:00Z","updatedAt":"2026-06-28T10:05:00Z"}""";
        var client = CreateClient(new RecordingHandler(_ => Json(body)));
        var result = await client.GetAsync("prop-1");
        Assert.Equal(OperateSectionStatus.Allowed, result.Status);
        Assert.Equal(label, ConsoleProposalPresentation.StatusLabel(result.Value!.Status));
        Assert.Equal(terminal, result.Value.IsTerminal);
        Assert.Equal(terminal, result.Value.ToSummary().IsTerminal);
    }

    [Fact]
    public async Task ListBindsLiveProposalsEndpoint_AndMapsSummaries()
    {
        const string body = """
        {
          "proposals": [
            {
              "proposalId": "prop-1",
              "kind": "MetadataRelease",
              "status": "AwaitingApproval",
              "requestedBy": "agent.ingest",
              "requestedByAgent": "agent.ingest",
              "summary": "Promote parcels to prod",
              "findingId": "finding-release-skew",
              "autonomyRule": "platform-release-skew",
              "actionDiscriminator": "release.converge",
              "riskLevel": "Medium",
              "createdAt": "2026-06-28T10:00:00Z",
              "updatedAt": "2026-06-28T10:05:00Z"
            },
            {
              "proposalId": "prop-2",
              "kind": "Deploy",
              "status": "Submitted",
              "summary": "Upgrade server to v21",
              "riskLevel": "High",
              "createdAt": "2026-06-28T09:00:00Z",
              "updatedAt": "2026-06-28T09:05:00Z"
            }
          ]
        }
        """;
        var handler = new RecordingHandler(_ => Json(body));
        var client = CreateClient(handler, adminApiKey: "admin-key");

        var result = await client.ListAsync();

        Assert.Equal(OperateSectionStatus.Allowed, result.Status);
        Assert.Equal(2, result.Value!.Count);

        var first = result.Value![0];
        Assert.Equal("prop-1", first.ProposalId);
        Assert.Equal(ConsoleProposalKind.MetadataRelease, first.Kind);
        Assert.Equal(ConsoleProposalStatus.AwaitingApproval, first.Status);
        Assert.Equal(ConsoleProposalRisk.Medium, first.RiskLevel);
        Assert.Equal("agent.ingest", first.RequestedBy);
        Assert.Equal("finding-release-skew", first.FindingId);
        Assert.Equal("platform-release-skew", first.AutonomyRule);
        Assert.Equal("release.converge", first.ActionDiscriminator);

        var request = Assert.Single(handler.Requests);
        Assert.EndsWith("/api/v1/admin/proposals", request.RequestUri!.AbsolutePath, StringComparison.Ordinal);
        Assert.True(request.Headers.TryGetValues("X-API-Key", out var keys) && keys.Single() == "admin-key");
    }

    [Fact]
    public async Task ListForwardsStatusAndKindFiltersToQuery()
    {
        var handler = new RecordingHandler(_ => Json("""{ "proposals": [] }"""));
        var client = CreateClient(handler);

        await client.ListAsync(status: "AwaitingApproval", kind: "Deploy");

        var request = Assert.Single(handler.Requests);
        var query = request.RequestUri!.Query;
        Assert.Contains("status=AwaitingApproval", query, StringComparison.Ordinal);
        Assert.Contains("kind=Deploy", query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetBindsDetailEndpoint_AndMapsPlanDiffDryRunBlockers()
    {
        const string body = """
        {
          "proposalId": "prop-1",
          "kind": "DataImport",
          "status": "AwaitingApproval",
          "requestedBy": "agent.ingest",
          "summary": "Import parcels.gpkg",
          "findingId": "finding-import",
          "autonomyRule": "import-backlog",
          "actionDiscriminator": "imports.retry",
          "diff": ["+ layer parcels", "+ 12,345 features"],
          "dryRun": ["estimated 12s", "no destructive ops"],
          "riskLevel": "Low",
          "blockingReasons": [],
          "warnings": ["CRS assumed EPSG:4326"],
          "guardrailTier": "RequiresApproval",
          "createdAt": "2026-06-28T10:00:00Z",
          "updatedAt": "2026-06-28T10:05:00Z"
        }
        """;
        var handler = new RecordingHandler(_ => Json(body));
        var client = CreateClient(handler);

        var result = await client.GetAsync("prop-1");

        Assert.Equal(OperateSectionStatus.Allowed, result.Status);
        var detail = result.Value!;
        Assert.Equal(ConsoleProposalKind.DataImport, detail.Kind);
        Assert.Equal(2, detail.Diff.Count);
        Assert.Equal(2, detail.DryRun.Count);
        Assert.Single(detail.Warnings);
        Assert.Empty(detail.BlockingReasons);
        Assert.Equal("RequiresApproval", detail.GuardrailTier);
        Assert.Equal("finding-import", detail.FindingId);
        Assert.Equal("import-backlog", detail.AutonomyRule);
        Assert.Equal("imports.retry", detail.ActionDiscriminator);
        Assert.Equal("finding-import", detail.ToSummary().FindingId);

        var request = Assert.Single(handler.Requests);
        Assert.EndsWith("/api/v1/admin/proposals/prop-1", request.RequestUri!.AbsolutePath, StringComparison.Ordinal);
    }

    private const string SubmittedDetail = """
        {
          "proposalId": "prop-1",
          "version": 3,
          "planHash": "plan-3",
          "kind": "Deploy",
          "status": "Submitted",
          "summary": "Upgrade server to v21",
          "diff": [],
          "dryRun": [],
          "riskLevel": "High",
          "blockingReasons": [],
          "warnings": [],
          "resolvedBy": "operator.alice",
          "executionOperationId": "deploy-op-7",
          "createdAt": "2026-06-28T10:00:00Z",
          "updatedAt": "2026-06-28T10:10:00Z",
          "resolvedAt": "2026-06-28T10:10:00Z"
        }
        """;

    private static string Review(string proposalId = "prop-1", long version = 3, string planHash = "plan-3") => $$"""
        {
          "proposal": { "proposalId": "{{proposalId}}", "version": {{version}}, "planHash": "{{planHash}}", "kind": "Deploy",
            "status": "AwaitingApproval", "summary": "Upgrade", "createdAt": "2026-06-28T10:00:00Z", "updatedAt": "2026-06-28T10:05:00Z" },
          "review": { "tenantId": null, "proposalId": "{{proposalId}}", "proposalVersion": {{version}}, "planHash": "{{planHash}}",
            "expiresAt": "2026-06-28T10:15:00Z", "capability": "cap-for-{{proposalId}}-{{version}}" }
        }
        """;

    private static RecordingHandler ReviewThenApprove(string review, Func<HttpRequestMessage, HttpResponseMessage>? approve = null) =>
        new(request => request.RequestUri!.AbsolutePath.EndsWith("/approval-review", StringComparison.Ordinal)
            ? Json(review)
            : (approve ?? (_ => Json(SubmittedDetail)))(request));

    [Fact]
    public async Task ApproveOpensHumanReviewThenSendsItsCapability_AndReturnsResolvedDetail()
    {
        var handler = ReviewThenApprove(Review());
        var client = CreateClient(handler, sessions: BearerSessions());

        var result = await client.ApproveAsync("prop-1", new ConsoleProposalReviewedPlan(3, "plan-3"));

        Assert.Equal(OperateSectionStatus.Allowed, result.Status);
        Assert.Equal(ConsoleProposalStatus.Submitted, result.Value!.Status);
        Assert.Equal("deploy-op-7", result.Value!.ExecutionOperationId);
        Assert.Equal(3, result.Value.Version);
        Assert.Equal("plan-3", result.Value.PlanHash);

        Assert.Collection(
            handler.Requests,
            review =>
            {
                Assert.Equal(HttpMethod.Get, review.Method);
                Assert.EndsWith("/api/v1/admin/proposals/prop-1/approval-review", review.RequestUri!.AbsolutePath, StringComparison.Ordinal);
                Assert.False(review.Headers.Contains("X-Honua-Approval-Capability"));
            },
            approve =>
            {
                Assert.Equal(HttpMethod.Post, approve.Method);
                Assert.EndsWith("/api/v1/admin/proposals/prop-1/approve", approve.RequestUri!.AbsolutePath, StringComparison.Ordinal);
                Assert.Equal("cap-for-prop-1-3", Assert.Single(approve.Headers.GetValues("X-Honua-Approval-Capability")));
            });
    }

    // The server refuses approval over any explicit credential; only the ambient human session decides.
    [Theory]
    [InlineData(ConsoleServerCredentialMode.Interactive, ConsoleAccountAuthMode.AccountRbac)]
    [InlineData(ConsoleServerCredentialMode.HeadlessService, ConsoleAccountAuthMode.ServiceApiKey)]
    public async Task ApproveNeverSendsBearerOrAdminKey(ConsoleServerCredentialMode mode, ConsoleAccountAuthMode authMode)
    {
        var handler = ReviewThenApprove(Review());
        var client = CreateClient(handler, adminApiKey: "shared-admin-key", sessions: BearerSessions(),
            credentialMode: mode, accountAuthMode: authMode);

        _ = await client.ApproveAsync("prop-1");

        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, request =>
        {
            Assert.Null(request.Headers.Authorization);
            Assert.False(request.Headers.Contains("X-API-Key"));
            Assert.DoesNotContain(request.Headers, header => header.Key.Contains("Actor", StringComparison.OrdinalIgnoreCase));
        });
    }

    [Theory]
    [InlineData(4, "plan-3")]
    [InlineData(3, "plan-other")]
    public async Task ApproveRefusesWhenThePlanChangedSinceItWasReviewed(long serverVersion, string serverHash)
    {
        var handler = ReviewThenApprove(Review(version: serverVersion, planHash: serverHash),
            _ => throw new InvalidOperationException("Approve must not be sent."));
        var client = CreateClient(handler);

        var result = await client.ApproveAsync("prop-1", new ConsoleProposalReviewedPlan(3, "plan-3"));

        Assert.False(result.IsAllowed);
        Assert.Contains("changed", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData("""{ "proposal": null, "review": null }""")]
    [InlineData("""{ "review": { "proposalId": "prop-1", "proposalVersion": 3, "capability": "" } }""")]
    [InlineData("""{ "review": { "proposalId": "prop-other", "proposalVersion": 3, "capability": "cap" } }""")]
    public async Task ApproveFailsClosedWithoutAMatchingCapability(string review)
    {
        var handler = ReviewThenApprove(review, _ => throw new InvalidOperationException("Approve must not be sent."));
        var client = CreateClient(handler);

        var result = await client.ApproveAsync("prop-1");

        Assert.Equal(OperateSectionStatus.Unavailable, result.Status);
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ApproveHumanSessionRefusalExplainsTheBrowserSessionRequirement(bool refuseReview)
    {
        var handler = new RecordingHandler(request =>
            refuseReview || !request.RequestUri!.AbsolutePath.EndsWith("/approval-review", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.Forbidden)
                : Json(Review()));
        var client = CreateClient(handler, sessions: BearerSessions());

        var result = await client.ApproveAsync("prop-1");

        Assert.Equal(OperateSectionStatus.Forbidden, result.Status);
        Assert.Null(result.Value);
        Assert.Contains("live signed-in browser session", result.Message, StringComparison.Ordinal);
        Assert.Contains("'approve' grant", result.Message, StringComparison.Ordinal);
        Assert.Equal(refuseReview ? 1 : 2, handler.Requests.Count);
    }

    [Fact]
    public async Task ApproveFailurePreservesProblemDetailAndComparableIdentifiers()
    {
        var handler = ReviewThenApprove(Review("prop-identity-1"), _ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Conflict)
            {
                ReasonPhrase = "Conflict",
                Content = new StringContent(
                    """
                    {
                      "title": "Approval denied",
                      "detail": "The proposal is no longer awaiting approval.",
                      "code": "proposal-resolved",
                      "proposalId": "prop-identity-1",
                      "operationId": "op-identity-1",
                      "correlationId": "corr-identity-1"
                    }
                    """,
                    Encoding.UTF8,
                    "application/problem+json")
            };
            response.Headers.TryAddWithoutValidation("X-Request-ID", "request-identity-1");
            return response;
        });
        var client = CreateClient(handler, sessions: BearerSessions());

        var result = await client.ApproveAsync("prop-identity-1");

        Assert.Equal(OperateSectionStatus.Unavailable, result.Status);
        Assert.Equal("The proposal is no longer awaiting approval.", result.Message);
        Assert.Contains("code=proposal-resolved", result.Detail, StringComparison.Ordinal);
        Assert.Contains("proposalId=prop-identity-1", result.Detail, StringComparison.Ordinal);
        Assert.Contains("operationId=op-identity-1", result.Detail, StringComparison.Ordinal);
        Assert.Contains("correlationId=corr-identity-1", result.Detail, StringComparison.Ordinal);
        Assert.Contains("requestId=request-identity-1", result.Detail, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ConsoleServerCredentialMode.Interactive)]
    [InlineData(ConsoleServerCredentialMode.HeadlessService)]
    public async Task RejectUsesTheHostBrowserTransportWhenItOwnsCredentials(ConsoleServerCredentialMode mode)
    {
        var handler = new RecordingHandler(_ => Json(SubmittedDetail));
        var profiles = new InMemoryConsoleEnvironmentProfileStore(
            [Profile(ConsoleAccountAuthMode.ServiceApiKey)], activeProfileId: "live");
        var client = new HttpConsoleProposalsClient(new HttpClient(handler), profiles, new InMemoryConsoleAccountSessionStore(),
            adminApiKey: "shared-admin-key", operatorBearerProvider: new BrowserTransportBearers(), credentialMode: mode);

        var result = await client.RejectAsync("prop-1", "Out of window");

        Assert.Equal(OperateSectionStatus.Allowed, result.Status);
        var request = Assert.Single(handler.Requests);
        Assert.Null(request.Headers.Authorization);
        Assert.False(request.Headers.Contains("X-API-Key"));
    }

    private sealed class BrowserTransportBearers : IConsoleOperatorBearerProvider
    {
        public Task<ConsoleOperatorBearerResolution> ResolveAsync(ConsoleEnvironmentProfile profile, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ConsoleOperatorBearerResolution { HasInteractiveSession = true, UsesBrowserTransport = true });
    }

    [Fact]
    public async Task RejectPostsReasonBody_ToRejectEndpoint()
    {
        const string body = """
        {
          "proposalId": "prop-1",
          "kind": "Deploy",
          "status": "Rejected",
          "summary": "Upgrade server to v21",
          "diff": [],
          "dryRun": [],
          "riskLevel": "High",
          "blockingReasons": [],
          "warnings": [],
          "resolvedBy": "operator.alice",
          "resolutionReason": "Out of change window",
          "createdAt": "2026-06-28T10:00:00Z",
          "updatedAt": "2026-06-28T10:10:00Z"
        }
        """;
        string? capturedBody = null;
        var handler = new RecordingHandler(req =>
        {
            capturedBody = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return Json(body);
        });
        var client = CreateClient(handler, sessions: BearerSessions());

        var result = await client.RejectAsync("prop-1", "Out of change window");

        Assert.Equal(OperateSectionStatus.Allowed, result.Status);
        Assert.Equal(ConsoleProposalStatus.Rejected, result.Value!.Status);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.EndsWith("/api/v1/admin/proposals/prop-1/reject", request.RequestUri!.AbsolutePath, StringComparison.Ordinal);
        Assert.Contains("Out of change window", capturedBody!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RejectWithoutReasonFailsClosed_WithoutCallingServer()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = CreateClient(handler);

        var result = await client.RejectAsync("prop-1", "   ");

        Assert.False(result.IsAllowed);
        Assert.Contains("reason is required", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ReadsReturnMissingBindingWhenNoEnvironmentIsConnected()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var profiles = new InMemoryConsoleEnvironmentProfileStore([], activeProfileId: null);
        var client = new HttpConsoleProposalsClient(
            new HttpClient(handler),
            profiles,
            new InMemoryConsoleAccountSessionStore(),
            adminApiKey: "admin-key");

        var list = await client.ListAsync();
        var detail = await client.GetAsync("prop-1");
        var approve = await client.ApproveAsync("prop-1");

        Assert.Equal(OperateSectionStatus.Unavailable, list.Status);
        Assert.Equal(OperateSectionStatus.Unavailable, detail.Status);
        Assert.Equal(OperateSectionStatus.Unavailable, approve.Status);
        Assert.Empty(handler.Requests);
    }

    private static HttpConsoleProposalsClient CreateClient(
        HttpMessageHandler handler,
        string? adminApiKey = null,
        IConsoleAccountSessionStore? sessions = null,
        ConsoleServerCredentialMode credentialMode = ConsoleServerCredentialMode.Interactive,
        ConsoleAccountAuthMode accountAuthMode = ConsoleAccountAuthMode.AccountRbac)
    {
        var profile = Profile(accountAuthMode);
        var profiles = new InMemoryConsoleEnvironmentProfileStore([profile], activeProfileId: profile.Id);
        return new HttpConsoleProposalsClient(
            new HttpClient(handler),
            profiles,
            sessions ?? new InMemoryConsoleAccountSessionStore(),
            adminApiKey,
            credentialMode: credentialMode);
    }

    private static ConsoleEnvironmentProfile Profile(ConsoleAccountAuthMode accountAuthMode = ConsoleAccountAuthMode.AccountRbac) => new()
    {
        Id = "live",
        DisplayName = "Live Server Alpha",
        ServerBaseUri = new Uri("https://server.example"),
        UpdatedAt = DateTimeOffset.Parse("2026-06-28T10:00:00Z"),
        Account = new ConsoleAccountBinding
        {
            AuthMode = accountAuthMode,
            AccountId = "operator.live"
        }
    };

    private static IConsoleAccountSessionStore BearerSessions()
    {
        var sessions = new InMemoryConsoleAccountSessionStore();
        sessions.SaveSessionAsync(new ConsoleAccountSession
        {
            ProfileId = "live",
            AccessToken = "operator-test-bearer"
        }).GetAwaiter().GetResult();
        return sessions;
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(CloneRequest(request));
            return Task.FromResult(responder(request));
        }

        private static HttpRequestMessage CloneRequest(HttpRequestMessage request)
        {
            var clone = new HttpRequestMessage(request.Method, request.RequestUri);
            foreach (var header in request.Headers)
            {
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            return clone;
        }
    }
}
