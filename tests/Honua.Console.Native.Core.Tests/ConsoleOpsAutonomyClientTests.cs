using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Security;
using Honua.Console.Shell.Services;

namespace Honua.Console.Native.Core.Tests;

/// <summary>
/// Contract tests for the narrow graduated-autonomy client. Reads may use the configured
/// admin credential, while every human policy/settings mutation must retain the active
/// operator bearer and fail closed when that bearer is unavailable.
/// </summary>
public sealed class ConsoleOpsAutonomyClientTests
{
    [Fact]
    public async Task LoadReadsOneBoundedAuditPageAndMapsBothTypeIdStreams()
    {
        var handler = new RecordingHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/v1/admin/observability/autonomy/policies" => JsonResponse(BuildPolicies(),
                OpsAutonomyJsonContext.Default.OpsAutonomyPolicyListResponse),
            "/api/v1/admin/observability/autonomy/settings" => JsonResponse(BuildSettings(),
                OpsAutonomyJsonContext.Default.OpsAutonomySettingsResponse),
            "/api/v1/admin/observability/events" => JsonResponse(BuildAuditPage(),
                OperateObservabilityJsonContext.Default.OperateEventPageResponse),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });
        var client = CreateClient(handler, adminApiKey: "read-key");

        var result = await client.LoadAsync();

        Assert.Equal(OperateSectionStatus.Allowed, result.Status);
        Assert.False(result.Value!.Settings.KillSwitchEnabled);
        var policy = Assert.Single(result.Value.Policies);
        Assert.Equal("alert-dispatch-backlog", policy.Rule);
        Assert.False(policy.IsPersisted);
        Assert.Collection(
            result.Value.AuditEntries,
            entry => Assert.Equal("operation.auto_verified", entry.Action),
            entry => Assert.Equal("ops_autonomy.policy.update", entry.Action));

        Assert.Equal(3, handler.Requests.Count);
        Assert.All(handler.Requests, request =>
            Assert.True(request.Headers.TryGetValues("X-API-Key", out var values)
                && values.Single() == "read-key"));
        var auditRequest = Assert.Single(handler.Requests, request =>
            request.RequestUri!.AbsolutePath == "/api/v1/admin/observability/events");
        Assert.Contains("kind=Audit", auditRequest.RequestUri!.Query, StringComparison.Ordinal);
        Assert.Contains("pageSize=100", auditRequest.RequestUri.Query, StringComparison.Ordinal);
        Assert.DoesNotContain("resourceRef=", auditRequest.RequestUri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadOlderServerReturnsUnsupportedAndDoesNotInventPolicyState()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var client = CreateClient(handler, adminApiKey: "read-key");

        var result = await client.LoadAsync();

        Assert.Equal(OperateSectionStatus.Unsupported, result.Status);
        Assert.Null(result.Value);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task LoadDurableControlPlaneUnavailableStaysUnavailableNotUnsupported()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var client = CreateClient(handler, adminApiKey: "read-key");

        var result = await client.LoadAsync();

        Assert.Equal(OperateSectionStatus.Unavailable, result.Status);
        Assert.Null(result.Value);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task LoadMissingAuditFeedKeepsControlsAndLabelsHistoryUnavailable()
    {
        var handler = new RecordingHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/v1/admin/observability/autonomy/policies" => JsonResponse(BuildPolicies(),
                OpsAutonomyJsonContext.Default.OpsAutonomyPolicyListResponse),
            "/api/v1/admin/observability/autonomy/settings" => JsonResponse(BuildSettings(),
                OpsAutonomyJsonContext.Default.OpsAutonomySettingsResponse),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });
        var client = CreateClient(handler, adminApiKey: "read-key");

        var result = await client.LoadAsync();

        Assert.Equal(OperateSectionStatus.Allowed, result.Status);
        Assert.NotNull(result.Value);
        Assert.True(result.Value.AuditPartialResult);
        Assert.Contains("audit feed", result.Value.AuditMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("findings remain propose-only", result.Value.AuditMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SetPolicyModeUsesOperatorBearerAndReturnsServerConfirmedMode()
    {
        var handler = new RecordingHandler(request => JsonResponse(
            BuildPolicies().Policies.Single() with { Mode = "ProposeOnly" },
            OpsAutonomyJsonContext.Default.OpsAutonomyPolicyResponse));
        var client = CreateClient(
            handler,
            adminApiKey: "shared-admin-key",
            sessions: BearerSessions());

        var result = await client.SetPolicyModeAsync(
            "alert-dispatch-backlog",
            "AutoApply",
            "Graduated after review");

        Assert.Equal(OperateSectionStatus.Allowed, result.Status);
        Assert.Equal("ProposeOnly", result.Value!.Mode);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Equal(
            new AuthenticationHeaderValue("Bearer", "operator-test-bearer"),
            request.Headers.Authorization);
        Assert.False(request.Headers.Contains("X-API-Key"));
        Assert.Contains("\"mode\":\"AutoApply\"", handler.Bodies.Single(), StringComparison.Ordinal);
        Assert.Contains("Graduated after review", handler.Bodies.Single(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SetKillSwitchInteractiveSentinelFailsClosedWithoutSendingRequest()
    {
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("Request must not be sent."));
        var sessions = new InMemoryConsoleAccountSessionStore();
        await sessions.SaveSessionAsync(new ConsoleAccountSession
        {
            ProfileId = "live",
            AccessToken = ConsoleAuthConstants.SessionSentinelPrefix + "live"
        });
        var client = CreateClient(handler, adminApiKey: "shared-admin-key", sessions: sessions);

        var result = await client.SetKillSwitchAsync(true, "Stop autonomous remediation");

        Assert.Equal(OperateSectionStatus.Forbidden, result.Status);
        Assert.Contains("sign in", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void AuditMapperBuildsCausalFieldsAndKeepsUnknownEvidenceHonest()
    {
        var mapped = OpsAutonomyAuditMapper.Map(
        [
            new OperateEventResponse
            {
                EventId = "audit:91",
                Kind = "audit",
                Severity = "notice",
                OccurredAt = DateTimeOffset.Parse("2026-07-10T10:00:00Z"),
                Title = "operation.auto_applied",
                Actor = "ops-autonomy",
                CorrelationId = "operation-42",
                ResourceRef = "operation_autonomy/finding-42",
                DetailsJson = "{\"rule\":\"alert-dispatch-backlog\",\"findingId\":\"finding-42\",\"evidenceRefs\":[\"dispatch:dead-letter:7\"],\"status\":\"Succeeded\",\"operationId\":\"operation-42\",\"kind\":\"AdminConfigChange\",\"actionDiscriminator\":\"alerts.redrive_dead_letters\"}"
            },
            new OperateEventResponse
            {
                EventId = "audit:90",
                Kind = "audit",
                Severity = "notice",
                OccurredAt = DateTimeOffset.Parse("2026-07-10T09:59:00Z"),
                Title = "ops_autonomy.settings.update",
                Actor = "operator.alice",
                CorrelationId = "global",
                ResourceRef = "ops_autonomy_policy/global"
            }
        ]);

        var action = mapped[0];
        Assert.Equal("finding-42", action.FindingId);
        Assert.Equal("alert-dispatch-backlog", action.Rule);
        Assert.Equal("Auto-applied", action.Outcome.Label);
        Assert.Equal("dispatch:dead-letter:7", Assert.Single(action.EvidenceRefs));
        Assert.Equal("Status: Succeeded; Operation: operation-42.", action.OutcomeEvidence);
        Assert.Equal("AdminConfigChange", action.OperationKind);
        Assert.Equal("alerts.redrive_dead_letters", action.ActionDiscriminator);
        Assert.Equal("operation-42", action.ToTimelineEntry().CorrelationId);

        var policy = mapped[1];
        Assert.True(policy.IsPolicyChange);
        Assert.Empty(policy.EvidenceRefs);
        Assert.Equal("Evidence detail was not projected by this server event.", policy.OutcomeEvidence);
    }

    [Fact]
    public void AuditMapperBoundsEvidenceAndDistinguishesFailedVerificationAndCompensation()
    {
        var longRef = new string('x', 300);
        var refs = Enumerable.Range(0, 15)
            .Select(index => index == 1 ? "evidence-0" : index == 2 ? longRef : $"evidence-{index}")
            .ToArray();
        var details = JsonSerializer.Serialize(new
        {
            findingId = "finding-42",
            message = "Backlog remained above the safe threshold.",
            evidenceRefs = refs
        });

        var mapped = OpsAutonomyAuditMapper.Map(
        [
            Audit("audit:1", "operation.auto_verified", "notice", details),
            Audit("audit:2", "operation.auto_verified", "error", details),
            Audit("audit:3", "operation.auto_compensated", "notice", details),
            Audit("audit:4", "operation.auto_compensated", "error", details)
        ]);

        Assert.Equal("Verified", mapped.Single(item => item.EventId == "audit:1").Outcome.Label);
        Assert.Equal("Verification failed", mapped.Single(item => item.EventId == "audit:2").Outcome.Label);
        Assert.True(mapped.Single(item => item.EventId == "audit:2").Outcome.Status.IsFailure);
        Assert.Equal("Compensated", mapped.Single(item => item.EventId == "audit:3").Outcome.Label);
        Assert.Equal("Compensation failed", mapped.Single(item => item.EventId == "audit:4").Outcome.Label);
        Assert.True(mapped.Single(item => item.EventId == "audit:4").Outcome.Status.IsFailure);

        var bounded = mapped[0];
        Assert.Equal(12, bounded.EvidenceRefs.Count);
        Assert.Equal(bounded.EvidenceRefs.Count, bounded.EvidenceRefs.Distinct(StringComparer.Ordinal).Count());
        Assert.All(bounded.EvidenceRefs, value => Assert.True(value.Length <= 256));
        Assert.Contains("Backlog remained above", bounded.OutcomeEvidence, StringComparison.Ordinal);
    }

    private static HttpConsoleOpsAutonomyClient CreateClient(
        HttpMessageHandler handler,
        string? adminApiKey = null,
        IConsoleAccountSessionStore? sessions = null)
    {
        var profile = new ConsoleEnvironmentProfile
        {
            Id = "live",
            DisplayName = "Live Server Alpha",
            ServerBaseUri = new Uri("https://server.example"),
            Account = new ConsoleAccountBinding
            {
                AuthMode = ConsoleAccountAuthMode.AccountRbac,
                AccountId = "operator.live"
            }
        };
        var profiles = new InMemoryConsoleEnvironmentProfileStore([profile], activeProfileId: profile.Id);
        var sessionStore = sessions ?? new InMemoryConsoleAccountSessionStore();
        return new HttpConsoleOpsAutonomyClient(
            new HttpClient(handler),
            profiles,
            sessionStore,
            adminApiKey);
    }

    private static OpsAutonomyPolicyListResponse BuildPolicies() => new()
    {
        GeneratedAt = DateTimeOffset.Parse("2026-07-10T10:00:00Z"),
        Policies =
        [
            new OpsAutonomyPolicyResponse
            {
                Rule = "alert-dispatch-backlog",
                Mode = "AutoApply",
                MaxAutoActionsPerWindow = 2,
                WindowSeconds = 3600,
                MaxBlastRadius = 1,
                UpdatedAt = DateTimeOffset.Parse("2026-07-10T09:00:00Z"),
                UpdatedBy = "operator.alice",
                IsPersisted = false,
                TrackRecord = new OpsAutonomyTrackRecordResponse
                {
                    ProposalsRaised = 12,
                    ProposalsApproved = 9,
                    ProposalsRejected = 3,
                    AutoApplied = 6,
                    RolledBack = 1,
                    Failed = 1,
                    FirstActivityAt = DateTimeOffset.Parse("2026-07-01T00:00:00Z"),
                    LastActivityAt = DateTimeOffset.Parse("2026-07-10T09:30:00Z")
                }
            }
        ]
    };

    private static OpsAutonomySettingsResponse BuildSettings() => new()
    {
        KillSwitchEnabled = false,
        UpdatedAt = DateTimeOffset.Parse("2026-07-10T09:00:00Z"),
        UpdatedBy = "operator.alice"
    };

    private static OperateEventPageResponse BuildAuditPage() => new()
    {
        Items =
        [
            new OperateEventResponse
            {
                EventId = "audit:41",
                Kind = "audit",
                Severity = "notice",
                OccurredAt = DateTimeOffset.Parse("2026-07-10T10:00:00Z"),
                Title = "operation.auto_verified",
                Actor = "ops-autonomy",
                CorrelationId = "operation-42",
                ResourceRef = "operation_autonomy/finding-42"
            },
            new OperateEventResponse
            {
                EventId = "audit:40",
                Kind = "audit",
                Severity = "notice",
                OccurredAt = DateTimeOffset.Parse("2026-07-10T09:00:00Z"),
                Title = "ops_autonomy.policy.update",
                Actor = "operator.alice",
                CorrelationId = "alert-dispatch-backlog",
                ResourceRef = "ops_autonomy_policy/alert-dispatch-backlog"
            },
            new OperateEventResponse
            {
                EventId = "audit:39",
                Kind = "audit",
                Severity = "notice",
                OccurredAt = DateTimeOffset.Parse("2026-07-10T08:00:00Z"),
                Title = "service.update",
                ResourceRef = "service/roads"
            }
        ]
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

    private static OperateEventResponse Audit(
        string eventId,
        string action,
        string severity,
        string detailsJson) => new()
        {
            EventId = eventId,
            Kind = "audit",
            Severity = severity,
            OccurredAt = DateTimeOffset.Parse("2026-07-10T10:00:00Z"),
            Title = action,
            CorrelationId = "operation-42",
            ResourceRef = "operation_autonomy/finding-42",
            DetailsJson = detailsJson
        };

    private static HttpResponseMessage JsonResponse<T>(
        T value,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo)
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

        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(CloneRequest(request));
            if (request.Content is not null)
            {
                Bodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));
            }

            return responder(request);
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
