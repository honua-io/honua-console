using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Security;
using Honua.Console.Shell.Services;

namespace Honua.Console.Shell.Tests;

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

    [Fact]
    public async Task ApproveWithoutDisplayedReview_RefusesForwardableOperatorBearer()
    {
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("No request may be forwarded."));
        var client = CreateClient(handler, adminApiKey: "shared-admin-key", sessions: BearerSessions());

        var result = await client.ApproveAsync("prop-1");

        Assert.Equal(OperateSectionStatus.Forbidden, result.Status);
        Assert.Contains("review", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ApproveInteractiveSentinelFailsClosedWithoutSharedAdminIdentity()
    {
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("Request must not be sent."));
        var sessions = new InMemoryConsoleAccountSessionStore();
        await sessions.SaveSessionAsync(new ConsoleAccountSession
        {
            ProfileId = "live",
            AccessToken = ConsoleAuthConstants.SessionSentinelPrefix + "live"
        });
        var client = CreateClient(handler, adminApiKey: "shared-admin-key", sessions: sessions);

        var result = await client.ApproveAsync("prop-1");

        Assert.Equal(OperateSectionStatus.Forbidden, result.Status);
        Assert.Contains("sign in", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ApproveExplicitHeadlessMode_RefusesSharedAdminKey()
    {
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("No request may be forwarded."));
        var client = CreateClient(handler, adminApiKey: "shared-admin-key",
            credentialMode: ConsoleServerCredentialMode.HeadlessService,
            accountAuthMode: ConsoleAccountAuthMode.ServiceApiKey);

        var result = await client.ApproveAsync("prop-1");
        var reviewed = await client.ApproveReviewedAsync(new("prop-1", 1, "plan", "environment"));

        Assert.Equal(OperateSectionStatus.Forbidden, result.Status);
        Assert.Equal(OperateSectionStatus.Forbidden, reviewed.Status);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ApproveForbiddenMapsToForbiddenResult_NotFabricatedSuccess()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        var client = CreateClient(handler, sessions: BearerSessions());

        var result = await client.ApproveAsync("prop-1");

        Assert.Equal(OperateSectionStatus.Forbidden, result.Status);
        Assert.Null(result.Value);
        Assert.Contains("approve", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RejectFailurePreservesProblemDetailAndComparableIdentifiers()
    {
        var handler = new RecordingHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                ReasonPhrase = "Forbidden",
                Content = new StringContent(
                    """
                    {
                      "title": "Approval denied",
                      "detail": "The current operation authority denied the decision.",
                      "code": "operation-authority",
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

        var result = await client.RejectAsync("prop-identity-1", "Human rejected the proposal");

        Assert.Equal(OperateSectionStatus.Forbidden, result.Status);
        Assert.Equal("The current operation authority denied the decision.", result.Message);
        Assert.Contains("code=operation-authority", result.Detail, StringComparison.Ordinal);
        Assert.Contains("proposalId=prop-identity-1", result.Detail, StringComparison.Ordinal);
        Assert.Contains("operationId=op-identity-1", result.Detail, StringComparison.Ordinal);
        Assert.Contains("correlationId=corr-identity-1", result.Detail, StringComparison.Ordinal);
        Assert.Contains("requestId=request-identity-1", result.Detail, StringComparison.Ordinal);
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

    [Fact]
    public async Task Get_PrivateManagementSession_DoesNotRequireAForwardableReadCredential()
    {
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("A bearer request must not be sent."));
        var human = new FixedHumanReview();
        var client = CreateClient(handler, humanSession: human, authority: ConsoleServerAuthority.Management);

        var result = await client.GetAsync("prop-1");

        Assert.True(result.IsAllowed);
        Assert.Equal(7, result.Value!.Version);
        Assert.Equal("private-environment", result.Value.HumanReviewEnvironment);
        Assert.Equal(ConsoleServerAuthority.Management, human.Authority);
        Assert.Contains("authority=Management", result.Value.HumanApprovalSignInPath, StringComparison.Ordinal);
        Assert.Empty(handler.Requests);
    }

    private sealed class FixedHumanReview : IConsoleHumanProposalSession
    {
        public ConsoleServerAuthority Authority { get; private set; }
        public Task<OperateSectionResult<ConsoleHumanProposalReview>> OpenReviewAsync(
            ConsoleEnvironmentProfile profile, ConsoleServerAuthority authority, string proposalId,
            CancellationToken cancellationToken = default)
        {
            Authority = authority;
            return Task.FromResult(OperateSectionResult<ConsoleHumanProposalReview>.Allowed(new(
                new ProposalDetailWire { ProposalId = proposalId, Version = 7, PlanHash = "plan", Status = "AwaitingApproval" },
                "private-environment", DateTimeOffset.UtcNow.AddMinutes(5))));
        }
        public Task<OperateSectionResult<ProposalDetailWire>> ApproveAsync(
            ConsoleEnvironmentProfile profile, ConsoleServerAuthority authority, ConsoleProposalDecision decision,
            CancellationToken cancellationToken = default) => Task.FromResult(
                OperateSectionResult<ProposalDetailWire>.Denied(OperateSectionStatus.Forbidden, "Not used."));
    }

    private static HttpConsoleProposalsClient CreateClient(
        HttpMessageHandler handler,
        string? adminApiKey = null,
        IConsoleAccountSessionStore? sessions = null,
        ConsoleServerCredentialMode credentialMode = ConsoleServerCredentialMode.Interactive,
        ConsoleAccountAuthMode accountAuthMode = ConsoleAccountAuthMode.AccountRbac,
        IConsoleHumanProposalSession? humanSession = null,
        ConsoleServerAuthority authority = ConsoleServerAuthority.Serving)
    {
        var profile = new ConsoleEnvironmentProfile
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
        var profiles = new InMemoryConsoleEnvironmentProfileStore([profile], activeProfileId: profile.Id);
        return new HttpConsoleProposalsClient(
            new HttpClient(handler),
            profiles,
            sessions ?? new InMemoryConsoleAccountSessionStore(),
            adminApiKey,
            credentialMode: credentialMode, humanSession: humanSession, authority: authority);
    }

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
