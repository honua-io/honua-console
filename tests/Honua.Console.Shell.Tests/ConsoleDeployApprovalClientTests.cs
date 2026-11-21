using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Security;
using Honua.Console.Shell.Services;

namespace Honua.Console.Shell.Tests;

public sealed class ConsoleDeployApprovalClientTests
{
    [Fact]
    public async Task ReviewRequiresBrowserSessionEvenWithBearerAndAdminKey()
    {
        var handler = new RecordingHandler(_ => JsonResponse(new DeployOperationResponse
        {
            OperationId = "deploy-1",
            Kind = "Deploy",
            Status = "Submitted"
        }));
        var client = CreateClient(handler, adminApiKey: "shared-admin-key", sessions: BearerSessions());

        var result = await client.OpenSubmitReviewAsync("deploy-1");

        Assert.Equal(OperateSectionStatus.Forbidden, result.Status);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task SubmitInteractiveSentinelFailsClosedWithoutSharedAdminIdentity()
    {
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("Request must not be sent."));
        var sessions = new InMemoryConsoleAccountSessionStore();
        await sessions.SaveSessionAsync(new ConsoleAccountSession
        {
            ProfileId = "live",
            AccessToken = ConsoleAuthConstants.SessionSentinelPrefix + "live"
        });
        var client = CreateClient(handler, adminApiKey: "shared-admin-key", sessions: sessions);

        var result = await client.OpenSubmitReviewAsync("deploy-1");

        Assert.Equal(OperateSectionStatus.Forbidden, result.Status);
        Assert.Contains("sign in", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task SubmitOpensHumanReviewThenSendsCapabilityWithoutBearerOrAdminKey()
    {
        var handler = ReviewThenAction("submit", "deploy-1", version: 4, planHash: "deploy-plan-4", status: "Submitted");
        var client = CreateClient(handler, adminApiKey: "shared-admin-key", sessions: BearerSessions());

        var result = await client.SubmitAsync(
            "deploy-1",
            "Reviewed by Alice",
            new ConsoleDeployReviewedOperation(4, "deploy-plan-4", "v21.1"));

        Assert.Equal(OperateSectionStatus.Allowed, result.Status);
        Assert.Equal(DeployOperationLifecycle.Submitted, result.Value!.Lifecycle);
        Assert.Collection(
            handler.Requests,
            review =>
            {
                Assert.Equal(HttpMethod.Get, review.Method);
                Assert.EndsWith("/api/v1/admin/deploy/operations/deploy-1/approval-review/submit", review.RequestUri!.AbsolutePath, StringComparison.Ordinal);
                Assert.Null(review.Headers.Authorization);
                Assert.False(review.Headers.Contains("X-API-Key"));
                Assert.False(review.Headers.Contains("X-Honua-Approval-Capability"));
            },
            submit =>
            {
                Assert.Equal(HttpMethod.Post, submit.Method);
                Assert.EndsWith("/api/v1/admin/deploy/operations/deploy-1/submit", submit.RequestUri!.AbsolutePath, StringComparison.Ordinal);
                Assert.Null(submit.Headers.Authorization);
                Assert.False(submit.Headers.Contains("X-API-Key"));
                Assert.Equal("cap-deploy-1-submit-4", Assert.Single(submit.Headers.GetValues("X-Honua-Approval-Capability")));
            });
    }

    [Fact]
    public async Task PromoteOpensHumanReviewThenSendsCapabilityWithoutBearerOrAdminKey()
    {
        var handler = ReviewThenAction("promote", "deploy-2", version: 7, planHash: "deploy-plan-7", status: "Succeeded");
        var client = CreateClient(
            handler,
            adminApiKey: "shared-admin-key",
            sessions: BearerSessions(),
            credentialMode: ConsoleServerCredentialMode.HeadlessService,
            authMode: ConsoleAccountAuthMode.ServiceApiKey);

        var result = await client.PromoteAsync(
            "deploy-2",
            "Canary verified",
            new ConsoleDeployReviewedOperation(7, "deploy-plan-7", "v21.1"));

        Assert.Equal(OperateSectionStatus.Allowed, result.Status);
        Assert.Equal(DeployOperationLifecycle.Succeeded, result.Value!.Lifecycle);
        Assert.Collection(
            handler.Requests,
            review =>
            {
                Assert.Equal(HttpMethod.Get, review.Method);
                Assert.EndsWith("/api/v1/admin/deploy/operations/deploy-2/approval-review/promote", review.RequestUri!.AbsolutePath, StringComparison.Ordinal);
                Assert.Null(review.Headers.Authorization);
                Assert.False(review.Headers.Contains("X-API-Key"));
            },
            promote =>
            {
                Assert.Equal(HttpMethod.Post, promote.Method);
                Assert.EndsWith("/api/v1/admin/deploy/operations/deploy-2/promote", promote.RequestUri!.AbsolutePath, StringComparison.Ordinal);
                Assert.Null(promote.Headers.Authorization);
                Assert.False(promote.Headers.Contains("X-API-Key"));
                Assert.Equal("cap-deploy-2-promote-7", Assert.Single(promote.Headers.GetValues("X-Honua-Approval-Capability")));
            });
    }

    [Theory]
    [InlineData(5, "deploy-plan-4", "v21.1")]
    [InlineData(4, "deploy-plan-other", "v21.1")]
    [InlineData(4, "deploy-plan-4", "v22.0")]
    public async Task SubmitRefusesBeforePostingWhenOperationChangedSinceReview(
        long serverVersion,
        string serverHash,
        string serverRevision)
    {
        var handler = new RecordingHandler(req =>
            req.RequestUri!.AbsolutePath.Contains("/approval-review/", StringComparison.Ordinal)
                ? ReviewJson("submit", "deploy-1", serverVersion, serverHash, serverRevision)
                : throw new InvalidOperationException("Submit must not be sent after drift."));
        var client = CreateClient(handler);

        var result = await client.SubmitAsync(
            "deploy-1",
            "Reviewed",
            new ConsoleDeployReviewedOperation(4, "deploy-plan-4", "v21.1"));

        Assert.False(result.IsAllowed);
        Assert.Contains("changed", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task SubmitFailsClosedWhenReviewGrantIsMissingCapability()
    {
        var handler = new RecordingHandler(req =>
            req.RequestUri!.AbsolutePath.Contains("/approval-review/", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"action":"submit","review":{"proposalId":"deploy-1","proposalVersion":1,"capability":""}}""", Encoding.UTF8, "application/json")
                }
                : throw new InvalidOperationException("Submit must not be sent without capability."));
        var client = CreateClient(handler);

        var result = await client.SubmitAsync("deploy-1", "Reviewed");

        Assert.Equal(OperateSectionStatus.Unavailable, result.Status);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task SubmitHumanSessionRefusalExplainsBrowserSessionRequirement()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        var client = CreateClient(handler, sessions: BearerSessions());

        var result = await client.SubmitAsync("deploy-1", "Reviewed");

        Assert.Equal(OperateSectionStatus.Forbidden, result.Status);
        Assert.Contains("live signed-in browser session", result.Message, StringComparison.Ordinal);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task SubmitFallsBackToLegacyBearerWhenServerHasNoApprovalReviewRoute()
    {
        var handler = new RecordingHandler(req =>
            req.RequestUri!.AbsolutePath.Contains("/approval-review/", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : JsonResponse(new DeployOperationResponse
                {
                    OperationId = "deploy-1",
                    Kind = "Deploy",
                    Status = "Submitted"
                }));
        var client = CreateClient(handler, adminApiKey: "shared-admin-key", sessions: BearerSessions());

        var result = await client.SubmitAsync("deploy-1", "Reviewed by Alice");

        Assert.Equal(OperateSectionStatus.Allowed, result.Status);
        Assert.Collection(
            handler.Requests,
            review => Assert.EndsWith("/approval-review/submit", review.RequestUri!.AbsolutePath, StringComparison.Ordinal),
            submit =>
            {
                Assert.Equal(HttpMethod.Post, submit.Method);
                Assert.Equal(new AuthenticationHeaderValue("Bearer", "operator-alice-bearer"), submit.Headers.Authorization);
                Assert.False(submit.Headers.Contains("X-API-Key"));
                Assert.False(submit.Headers.Contains("X-Honua-Approval-Capability"));
            });
    }

    [Fact]
    public async Task SubmitLegacyFallbackInteractiveSentinelFailsClosedWithoutSharedAdminIdentity()
    {
        var handler = new RecordingHandler(req =>
            req.RequestUri!.AbsolutePath.Contains("/approval-review/", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : throw new InvalidOperationException("Legacy submit must not be sent with sentinel."));
        var sessions = new InMemoryConsoleAccountSessionStore();
        await sessions.SaveSessionAsync(new ConsoleAccountSession
        {
            ProfileId = "live",
            AccessToken = ConsoleAuthConstants.SessionSentinelPrefix + "live"
        });
        var client = CreateClient(handler, adminApiKey: "shared-admin-key", sessions: sessions);

        var result = await client.SubmitAsync("deploy-1", "Approved by Alice");

        Assert.Equal(OperateSectionStatus.Forbidden, result.Status);
        Assert.Contains("sign in", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Single(handler.Requests);
    }

    private static HttpConsoleDeployApprovalClient CreateClient(
        HttpMessageHandler handler,
        string? adminApiKey = null,
        IConsoleAccountSessionStore? sessions = null,
        ConsoleServerCredentialMode credentialMode = ConsoleServerCredentialMode.Interactive,
        ConsoleAccountAuthMode authMode = ConsoleAccountAuthMode.AccountRbac)
    {
        var profile = new ConsoleEnvironmentProfile
        {
            Id = "live",
            DisplayName = "Live Server Alpha",
            ServerBaseUri = new Uri("https://server.example"),
            Account = new ConsoleAccountBinding
            {
                AuthMode = authMode,
                AccountId = "operator.live"
            }
        };
        return new HttpConsoleDeployApprovalClient(
            new HttpClient(handler),
            new InMemoryConsoleEnvironmentProfileStore([profile], activeProfileId: profile.Id),
            sessions ?? new InMemoryConsoleAccountSessionStore(),
            adminApiKey: adminApiKey,
            credentialMode: credentialMode);
    }

    private static IConsoleAccountSessionStore BearerSessions()
    {
        var sessions = new InMemoryConsoleAccountSessionStore();
        sessions.SaveSessionAsync(new ConsoleAccountSession
        {
            ProfileId = "live",
            AccessToken = "operator-alice-bearer"
        }).GetAwaiter().GetResult();
        return sessions;
    }

    private static RecordingHandler ReviewThenAction(
        string action,
        string operationId,
        long version,
        string planHash,
        string status) =>
        new(request =>
            request.RequestUri!.AbsolutePath.Contains("/approval-review/", StringComparison.Ordinal)
                ? ReviewJson(action, operationId, version, planHash, "v21.1")
                : JsonResponse(new DeployOperationResponse
                {
                    OperationId = operationId,
                    Version = version + 1,
                    Kind = "Deploy",
                    Status = status
                }));

    private static HttpResponseMessage ReviewJson(
        string action,
        string operationId,
        long version,
        string planHash,
        string desiredRevision)
    {
        var body = JsonSerializer.Serialize(
            new DeployHumanApprovalReviewResponse
            {
                Action = action,
                Operation = new DeployOperationResponse
                {
                    OperationId = operationId,
                    Version = version,
                    Kind = "Deploy",
                    Status = action == "submit" ? "AwaitingApproval" : "Reconciling",
                    Target = new DeployPlanTargetResponse { DesiredRevision = desiredRevision }
                },
                Review = new DeployHumanApprovalReviewGrant
                {
                    ProposalId = operationId,
                    ProposalVersion = version,
                    PlanHash = planHash,
                    ExpiresAt = DateTimeOffset.Parse("2026-06-28T10:15:00Z"),
                    Capability = $"cap-{operationId}-{action}-{version}"
                }
            },
            DeployControlJsonContext.Default.DeployHumanApprovalReviewResponse);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
    }

    private static HttpResponseMessage JsonResponse(DeployOperationResponse value)
    {
        var json = JsonSerializer.Serialize(value, MetadataReleaseJsonContext.Default.DeployOperationResponse);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
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
