using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.Native.Core.Tests;

/// <summary>
/// Recording-HttpClient unit tests proving the approval-surface proposals client (#193)
/// binds to the honua-server console approval REST API (honua-server #1694): list/filter,
/// detail (plan/diff/dry-run/risk/blockers), approve, and reject (reason required). Auth is
/// the admin X-API-Key; the RBAC approve gate / separation-of-duties 403 surfaces as a
/// Forbidden result and the client never fabricates data (charter §11).
/// </summary>
public sealed class ConsoleProposalsClientTests
{
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

        var request = Assert.Single(handler.Requests);
        Assert.EndsWith("/api/v1/admin/proposals/prop-1", request.RequestUri!.AbsolutePath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApprovePostsApproveEndpoint_AndReturnsResolvedDetail()
    {
        const string body = """
        {
          "proposalId": "prop-1",
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
        var handler = new RecordingHandler(_ => Json(body));
        var client = CreateClient(handler);

        var result = await client.ApproveAsync("prop-1");

        Assert.Equal(OperateSectionStatus.Allowed, result.Status);
        Assert.Equal(ConsoleProposalStatus.Submitted, result.Value!.Status);
        Assert.Equal("deploy-op-7", result.Value!.ExecutionOperationId);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.EndsWith("/api/v1/admin/proposals/prop-1/approve", request.RequestUri!.AbsolutePath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApprovePrefersSignedInOperatorBearer_AndLeavesAuditIdentityToServerClaims()
    {
        var handler = new RecordingHandler(_ => Json("""
        {
          "proposalId": "prop-1",
          "kind": "Deploy",
          "status": "Submitted",
          "summary": "Upgrade server",
          "diff": [],
          "dryRun": [],
          "riskLevel": "High",
          "blockingReasons": [],
          "warnings": [],
          "createdAt": "2026-06-28T10:00:00Z",
          "updatedAt": "2026-06-28T10:10:00Z"
        }
        """));
        var sessions = new InMemoryConsoleAccountSessionStore();
        await sessions.SaveSessionAsync(new ConsoleAccountSession
        {
            ProfileId = "live",
            AccessToken = "operator-alice-bearer"
        });
        var client = CreateClient(handler, adminApiKey: "shared-admin-key", sessions: sessions);

        _ = await client.ApproveAsync("prop-1");

        var request = Assert.Single(handler.Requests);
        Assert.Equal(new AuthenticationHeaderValue("Bearer", "operator-alice-bearer"), request.Headers.Authorization);
        Assert.False(request.Headers.Contains("X-API-Key"));
        Assert.DoesNotContain(request.Headers, header => header.Key.Contains("Actor", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ApproveForbiddenMapsToForbiddenResult_NotFabricatedSuccess()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        var client = CreateClient(handler);

        var result = await client.ApproveAsync("prop-1");

        Assert.Equal(OperateSectionStatus.Forbidden, result.Status);
        Assert.Null(result.Value);
        Assert.Contains("approve", result.Message, StringComparison.OrdinalIgnoreCase);
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
        var client = CreateClient(handler);

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
        IConsoleAccountSessionStore? sessions = null)
    {
        var profile = new ConsoleEnvironmentProfile
        {
            Id = "live",
            DisplayName = "Live Server Alpha",
            ServerBaseUri = new Uri("https://server.example"),
            UpdatedAt = DateTimeOffset.Parse("2026-06-28T10:00:00Z"),
            Account = new ConsoleAccountBinding
            {
                AuthMode = ConsoleAccountAuthMode.AccountRbac,
                AccountId = "operator.live"
            }
        };
        var profiles = new InMemoryConsoleEnvironmentProfileStore([profile], activeProfileId: profile.Id);
        return new HttpConsoleProposalsClient(
            new HttpClient(handler),
            profiles,
            sessions ?? new InMemoryConsoleAccountSessionStore(),
            adminApiKey);
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
