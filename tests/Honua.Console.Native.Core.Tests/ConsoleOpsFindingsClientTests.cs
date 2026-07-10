using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.Native.Core.Tests;

/// <summary>
/// Recording-HttpClient unit tests proving the ops-findings client binds to honua-server's
/// deterministic ops-findings endpoints (group /api/v1/admin/observability,
/// admin-authorized, bare JSON — NO ApiResponse envelope), maps findings, routes a propose
/// through the approval gateway, and treats a cleared-condition 404 as a stale-finding
/// refresh cue (Missing) rather than a hard failure. No model/LLM reasoning is involved.
/// </summary>
public sealed class ConsoleOpsFindingsClientTests
{
    [Fact]
    public async Task ListDeserializesFindingsAndAttachesAdminKey()
    {
        var handler = new RecordingHandler(request =>
            request.Method == HttpMethod.Get
                ? JsonResponse(BuildList(), OpsFindingsJsonContext.Default.OpsFindingsListResponse)
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        var client = CreateClient(handler, adminApiKey: "admin-key");

        var result = await client.ListAsync();

        Assert.Equal(OperateSectionStatus.Allowed, result.Status);
        Assert.Equal(2, result.Value!.Findings!.Count);

        var request = Assert.Single(handler.Requests);
        Assert.True(request.Headers.TryGetValues("X-API-Key", out var values) && values.Single() == "admin-key");
        Assert.EndsWith("/api/v1/admin/observability/findings", request.RequestUri!.AbsolutePath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProposeReturnsProposalCreatedWithProposalId()
    {
        var handler = new RecordingHandler(request =>
            request.Method == HttpMethod.Post
                ? JsonResponse(
                    new OpsFindingProposeResponse
                    {
                        FindingId = "platform-release-skew-abc123",
                        Status = "ProposalCreated",
                        ProposalId = "prop-42"
                    },
                    OpsFindingsJsonContext.Default.OpsFindingProposeResponse)
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        var client = CreateClient(handler);

        var result = await client.ProposeAsync("platform-release-skew-abc123");

        Assert.Equal(OperateSectionStatus.Allowed, result.Status);
        Assert.Equal("ProposalCreated", result.Value!.Status);
        Assert.Equal("prop-42", result.Value.ProposalId);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.EndsWith(
            "/api/v1/admin/observability/findings/platform-release-skew-abc123/propose",
            request.RequestUri!.AbsolutePath,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProposePrefersSignedInOperatorBearerOverConfiguredAdminKey()
    {
        var handler = new RecordingHandler(_ => JsonResponse(
            new OpsFindingProposeResponse
            {
                FindingId = "finding-1",
                Status = "ProposalCreated",
                ProposalId = "prop-42"
            },
            OpsFindingsJsonContext.Default.OpsFindingProposeResponse));
        var sessions = new InMemoryConsoleAccountSessionStore();
        await sessions.SaveSessionAsync(new ConsoleAccountSession
        {
            ProfileId = "live",
            AccessToken = "operator-alice-bearer"
        });
        var client = CreateClient(handler, adminApiKey: "shared-admin-key", sessions: sessions);

        _ = await client.ProposeAsync("finding-1");

        var request = Assert.Single(handler.Requests);
        Assert.Equal(new AuthenticationHeaderValue("Bearer", "operator-alice-bearer"), request.Headers.Authorization);
        Assert.False(request.Headers.Contains("X-API-Key"));
    }

    [Fact]
    public async Task ProposeClearedConditionMapsToMissingForRefresh()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var client = CreateClient(handler);

        var result = await client.ProposeAsync("gone-finding");

        Assert.Equal(OperateSectionStatus.Missing, result.Status);
        Assert.Null(result.Value);
        Assert.Contains("cleared", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ListNoProfileReturnsMissingBindingWithoutCallingServer()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var profiles = new InMemoryConsoleEnvironmentProfileStore([], activeProfileId: null);
        var client = new HttpConsoleOpsFindingsClient(
            new HttpClient(handler),
            profiles,
            new InMemoryConsoleAccountSessionStore(),
            adminApiKey: "admin-key");

        var result = await client.ListAsync();

        Assert.Equal(OperateSectionStatus.Unavailable, result.Status);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void MapperProjectsSeverityChipSubjectRowsAndAction()
    {
        var findings = OpsFindingsMapper.Map(BuildList());

        var skew = findings.Single(f => f.Rule == "platform-release-skew");
        Assert.Equal("warning", skew.Severity.NormalizedState);
        Assert.True(skew.HasAction);
        Assert.Equal("Deploy", skew.RecommendedAction!.Kind);
        Assert.Contains(skew.Subject, row => row.Label == "Release" && row.Value == "2026.06.1");
        Assert.Single(skew.EvidenceRefs);

        var info = findings.Single(f => f.Rule == "gp-queue-idle");
        Assert.Equal("info", info.Severity.NormalizedState);
        Assert.False(info.HasAction);
        // Only populated subject identifiers are surfaced.
        Assert.DoesNotContain(info.Subject, row => row.Label == "Channel");
    }

    private static HttpConsoleOpsFindingsClient CreateClient(
        HttpMessageHandler handler,
        string? adminApiKey = null,
        IConsoleAccountSessionStore? sessions = null)
    {
        var profile = new ConsoleEnvironmentProfile
        {
            Id = "live",
            DisplayName = "Live Server Alpha",
            ServerBaseUri = new Uri("https://server.example"),
            UpdatedAt = DateTimeOffset.Parse("2026-06-06T10:00:00Z"),
            Account = new ConsoleAccountBinding
            {
                AuthMode = ConsoleAccountAuthMode.AccountRbac,
                AccountId = "operator.live"
            }
        };
        var profiles = new InMemoryConsoleEnvironmentProfileStore([profile], activeProfileId: profile.Id);
        return new HttpConsoleOpsFindingsClient(
            new HttpClient(handler),
            profiles,
            sessions ?? new InMemoryConsoleAccountSessionStore(),
            adminApiKey);
    }

    private static OpsFindingsListResponse BuildList() => new()
    {
        GeneratedAt = DateTimeOffset.Parse("2026-06-06T10:00:00Z"),
        Findings =
        [
            new OpsFindingResponse
            {
                Id = "platform-release-skew-abc123",
                Rule = "platform-release-skew",
                Severity = "Warning",
                Title = "Platform planes are skewed from the declared release",
                Explanation = "The worker plane is not co-versioned with release 2026.06.1.",
                DetectedAt = DateTimeOffset.Parse("2026-06-06T09:59:00Z"),
                Subject = new OpsFindingSubjectResponse { ReleaseVersion = "2026.06.1" },
                EvidenceRefs = ["release:2026.06.1"],
                RecommendedAction = new OpsFindingActionResponse
                {
                    Kind = "Deploy",
                    Summary = "Roll the worker plane to the declared release.",
                    Reason = "Restore co-versioning before the next coordinated deploy."
                }
            },
            new OpsFindingResponse
            {
                Id = "gp-queue-idle-def456",
                Rule = "gp-queue-idle",
                Severity = "Info",
                Title = "Geoprocessing queue is idle",
                Explanation = "No active geoprocessing jobs in the current window.",
                DetectedAt = DateTimeOffset.Parse("2026-06-06T09:58:00Z"),
                Subject = new OpsFindingSubjectResponse { WorkloadId = "local" },
                EvidenceRefs = []
            }
        ]
    };

    private static HttpResponseMessage JsonResponse<T>(T value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo)
    {
        var json = JsonSerializer.Serialize(value, typeInfo);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

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
