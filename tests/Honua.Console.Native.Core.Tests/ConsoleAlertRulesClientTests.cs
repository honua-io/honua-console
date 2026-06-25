using System.Net;
using System.Text;
using System.Text.Json;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.Native.Core.Tests;

/// <summary>
/// Recording-HttpClient unit tests proving the alert-rule DEFINITION editor binds to the SHIPPED honua-server
/// alert-rule admin contract (honua-server#1169, /api/v{version}/admin/alerts/rules…): get a rule (+ health +
/// draft validation), save (PUT) a rule, block a save whose draft fails server validation, and report an
/// unreachable/denied server honestly rather than fabricating a rule (Console Patterns Charter section 11).
/// The data source is wired to a real HttpConsoleAlertRulesClient so the ApiResponse&lt;T&gt; envelope unwrap
/// and the conditionsJson&lt;-&gt;condition mapping are exercised end-to-end.
/// </summary>
public sealed class ConsoleAlertRulesClientTests
{
    private const long RuleId = 42;

    [Fact]
    public async Task GetRuleBindsDefinitionHealthAndValidationFromLiveServer()
    {
        var handler = new RecordingHandler(request => request switch
        {
            { Method: var m } when m == HttpMethod.Get && IsRuleHealth(request) =>
                Envelope(BuildHealth(), OperateObservabilityJsonContext.Default.AlertRuleHealthEnvelope),
            { Method: var m } when m == HttpMethod.Get && IsRule(request) =>
                Envelope(BuildDwellRule(), OperateObservabilityJsonContext.Default.AlertRuleEnvelope),
            { Method: var m } when m == HttpMethod.Post && IsRulesTest(request) =>
                Envelope(BuildValidation(isValid: true), OperateObservabilityJsonContext.Default.AlertRuleTestEnvelope),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });
        var data = CreateDataSource(handler, adminApiKey: "admin-key");

        var view = await data.GetRuleAsync(RuleId.ToString());

        Assert.Null(view.BindingState);
        var rule = view.Rule!;
        Assert.Equal("42", rule.RuleId);
        Assert.Equal("Trucks dwelling in depot", rule.Name);
        Assert.Equal("geofence:dwell", rule.RuleType);
        Assert.True(rule.Enabled);

        // conditionsJson {"dwellSeconds":300} -> 5 minutes; zoneId -> GeofenceZoneId.
        Assert.Equal("12", rule.Condition.GeofenceZoneId);
        Assert.Equal(5, rule.Condition.DwellMinutes);

        // Health populates the editor's evaluated/incident/failure counts.
        Assert.Equal(2, rule.ActiveIncidentCount);
        Assert.Equal(1, rule.DeliveryFailureCount);
        Assert.Equal(["slack", "email"], rule.DeliveryChannels);

        // Every request carries X-API-Key admin auth.
        Assert.NotEmpty(handler.Requests);
        Assert.All(handler.Requests, request =>
            Assert.True(request.Headers.TryGetValues("X-API-Key", out var values) && values.Single() == "admin-key"));
    }

    [Fact]
    public async Task GetThresholdRuleMapsConditionsJsonToConditionFields()
    {
        var handler = new RecordingHandler(request => request switch
        {
            { Method: var m } when m == HttpMethod.Get && IsRuleHealth(request) =>
                new HttpResponseMessage(HttpStatusCode.NotFound),
            { Method: var m } when m == HttpMethod.Get && IsRule(request) =>
                Envelope(BuildThresholdRule(), OperateObservabilityJsonContext.Default.AlertRuleEnvelope),
            { Method: var m } when m == HttpMethod.Post && IsRulesTest(request) =>
                Envelope(BuildValidation(isValid: true), OperateObservabilityJsonContext.Default.AlertRuleTestEnvelope),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });
        var data = CreateDataSource(handler);

        var view = await data.GetRuleAsync(RuleId.ToString());

        var condition = view.Rule!.Condition;
        Assert.Equal("speed", condition.Subject);
        Assert.Equal(">", condition.Operator);
        Assert.Equal("60", condition.Threshold);
        Assert.Null(condition.GeofenceZoneId);
        Assert.Null(condition.DwellMinutes);
    }

    [Fact]
    public async Task SaveRuleValidatesThenPutsAndReturnsPersistedRule()
    {
        AlertRuleRequest? putRequest = null;
        var handler = new RecordingHandler(request => request switch
        {
            { Method: var m } when m == HttpMethod.Get && IsRuleHealth(request) =>
                Envelope(BuildHealth(), OperateObservabilityJsonContext.Default.AlertRuleHealthEnvelope),
            { Method: var m } when m == HttpMethod.Get && IsRule(request) =>
                Envelope(BuildDwellRule(), OperateObservabilityJsonContext.Default.AlertRuleEnvelope),
            { Method: var m } when m == HttpMethod.Post && IsRulesTest(request) =>
                Envelope(BuildValidation(isValid: true), OperateObservabilityJsonContext.Default.AlertRuleTestEnvelope),
            { Method: var m } when m == HttpMethod.Put && IsRule(request) =>
                CaptureAndReturn(request, ref putRequest, BuildDwellRule() with { IsActive = false, ConditionsJson = "{\"dwellSeconds\":600}" }),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });
        var data = CreateDataSource(handler);

        var edit = new OperateAlertRuleEdit(
            RuleId: RuleId.ToString(),
            Name: "Trucks dwelling in depot",
            Enabled: false,
            Condition: new OperateAlertRuleCondition(
                Subject: "zone:dwell", Operator: "dwell", Threshold: string.Empty, Window: string.Empty,
                GeofenceZoneId: "12", DwellMinutes: 10),
            DeliveryChannels: ["slack", "email"]);

        var result = await data.SaveRuleAsync(edit);

        Assert.True(result.Succeeded);
        Assert.Null(result.BindingState);
        Assert.False(result.Rule!.Enabled);

        // The PUT preserved the immutable fields read from the current rule and
        // built conditionsJson from the edited dwell minutes (10 min -> 600 s).
        Assert.NotNull(putRequest);
        Assert.Equal("dwell", putRequest!.TriggerType);
        Assert.Equal(12, putRequest.ZoneId);
        Assert.Equal("vehicles", putRequest.ServiceId);
        Assert.Equal(600, putRequest.CooldownSeconds);
        Assert.Contains("\"dwellSeconds\":600", putRequest.ConditionsJson, StringComparison.Ordinal);

        // The save round-trip hit test (POST) before the persist (PUT).
        Assert.Contains(handler.Requests, r => r.Method == HttpMethod.Post && IsRulesTest(r));
        Assert.Contains(handler.Requests, r => r.Method == HttpMethod.Put && IsRule(r));
    }

    [Fact]
    public async Task SaveRuleBlocksWhenServerValidationFailsAndDoesNotPersist()
    {
        var handler = new RecordingHandler(request => request switch
        {
            { Method: var m } when m == HttpMethod.Get && IsRule(request) =>
                Envelope(BuildDwellRule(), OperateObservabilityJsonContext.Default.AlertRuleEnvelope),
            { Method: var m } when m == HttpMethod.Post && IsRulesTest(request) =>
                Envelope(BuildValidation(isValid: false), OperateObservabilityJsonContext.Default.AlertRuleTestEnvelope),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });
        var data = CreateDataSource(handler);

        var edit = new OperateAlertRuleEdit(
            RuleId: RuleId.ToString(),
            Name: "Trucks dwelling in depot",
            Enabled: true,
            Condition: new OperateAlertRuleCondition(
                Subject: "zone:dwell", Operator: "dwell", Threshold: string.Empty, Window: string.Empty,
                GeofenceZoneId: "12", DwellMinutes: 5),
            DeliveryChannels: ["slack"]);

        var result = await data.SaveRuleAsync(edit);

        Assert.False(result.Succeeded);
        Assert.NotNull(result.BindingState);
        Assert.Contains("ZoneId is required", result.BindingState!.Detail, StringComparison.Ordinal);

        // A draft that fails validation is never persisted.
        Assert.DoesNotContain(handler.Requests, r => r.Method == HttpMethod.Put);
        Assert.DoesNotContain(handler.Requests, r => r.Method == HttpMethod.Post && !IsRulesTest(r));
    }

    [Fact]
    public async Task SaveRuleBlocksWhenEditedConditionCannotBeRepresented()
    {
        var handler = new RecordingHandler(request =>
            request.Method == HttpMethod.Get && IsRule(request)
                ? Envelope(BuildThresholdRule(), OperateObservabilityJsonContext.Default.AlertRuleEnvelope)
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        var data = CreateDataSource(handler);

        // A threshold edit with a non-numeric threshold cannot be represented faithfully.
        var edit = new OperateAlertRuleEdit(
            RuleId: RuleId.ToString(),
            Name: "Speeding",
            Enabled: true,
            Condition: new OperateAlertRuleCondition(
                Subject: "speed", Operator: ">", Threshold: "fast", Window: string.Empty),
            DeliveryChannels: ["slack"]);

        var result = await data.SaveRuleAsync(edit);

        Assert.False(result.Succeeded);
        Assert.Contains("numeric threshold", result.BindingState!.Detail, StringComparison.Ordinal);
        // No test/persist call is made when the condition cannot be built.
        Assert.DoesNotContain(handler.Requests, r => r.Method == HttpMethod.Post);
        Assert.DoesNotContain(handler.Requests, r => r.Method == HttpMethod.Put);
    }

    [Fact]
    public async Task GetRuleReportsForbiddenHonestly()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        var data = CreateDataSource(handler);

        var view = await data.GetRuleAsync(RuleId.ToString());

        Assert.Null(view.Rule);
        Assert.NotNull(view.BindingState);
        Assert.Equal(OperateAlertRulesBindingState.Forbidden, view.BindingState!.State);
    }

    [Fact]
    public async Task GetRuleReportsMissingForUnknownRule()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var data = CreateDataSource(handler);

        var view = await data.GetRuleAsync(RuleId.ToString());

        Assert.Null(view.Rule);
        Assert.Equal(OperateAlertRulesBindingState.MissingBinding, view.BindingState!.State);
    }

    [Fact]
    public async Task GetRuleRejectsNonNumericRuleIdWithoutCallingServer()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var data = CreateDataSource(handler);

        var view = await data.GetRuleAsync("not-a-number");

        Assert.Null(view.Rule);
        Assert.Equal(OperateAlertRulesBindingState.MissingBinding, view.BindingState!.State);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ReadsReturnMissingBindingWhenNoEnvironmentIsConnected()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var profiles = new InMemoryConsoleEnvironmentProfileStore([], activeProfileId: null);
        var client = new HttpConsoleAlertRulesClient(new HttpClient(handler), profiles, adminApiKey: "admin-key");

        var result = await client.GetRuleAsync(RuleId);

        Assert.Equal(OperateSectionStatus.Unavailable, result.Status);
        Assert.Empty(handler.Requests);
    }

    // --- Wiring helpers ------------------------------------------------------

    private static ServerOperateAlertRulesDataSource CreateDataSource(HttpMessageHandler handler, string? adminApiKey = null)
    {
        var profile = new ConsoleEnvironmentProfile
        {
            Id = "live",
            DisplayName = "Live Server Alpha",
            ServerBaseUri = new Uri("https://server.example"),
            UpdatedAt = DateTimeOffset.Parse("2026-05-24T19:00:00Z"),
            Account = new ConsoleAccountBinding
            {
                AuthMode = ConsoleAccountAuthMode.AccountRbac,
                AccountId = "operator.live"
            }
        };
        var profiles = new InMemoryConsoleEnvironmentProfileStore([profile], activeProfileId: profile.Id);
        var client = new HttpConsoleAlertRulesClient(new HttpClient(handler), profiles, adminApiKey);
        return new ServerOperateAlertRulesDataSource(new StubObservabilityClient(), client);
    }

    private static bool IsRule(HttpRequestMessage request) =>
        request.RequestUri!.AbsolutePath.EndsWith($"/alerts/rules/{RuleId}", StringComparison.Ordinal);

    private static bool IsRuleHealth(HttpRequestMessage request) =>
        request.RequestUri!.AbsolutePath.EndsWith($"/alerts/rules/{RuleId}/health", StringComparison.Ordinal);

    private static bool IsRulesTest(HttpRequestMessage request) =>
        request.RequestUri!.AbsolutePath.EndsWith("/alerts/rules/test", StringComparison.Ordinal);

    private static HttpResponseMessage CaptureAndReturn(
        HttpRequestMessage request,
        ref AlertRuleRequest? captured,
        AlertRuleResponse response)
    {
        var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
        captured = JsonSerializer.Deserialize(body, AlertAdminJsonContext.Default.AlertRuleRequest);
        return Envelope(response, OperateObservabilityJsonContext.Default.AlertRuleEnvelope);
    }

    // --- Fixtures ------------------------------------------------------------

    private static AlertRuleResponse BuildDwellRule() => new()
    {
        RuleId = RuleId,
        ServiceId = "vehicles",
        LayerId = 0,
        ZoneId = 12,
        RuleName = "Trucks dwelling in depot",
        TriggerType = "dwell",
        ConditionsJson = "{\"dwellSeconds\":300}",
        CooldownSeconds = 600,
        Severity = "warning",
        EditionRequired = "pro",
        Channels = ["slack", "email"],
        IsActive = true
    };

    private static AlertRuleResponse BuildThresholdRule() => new()
    {
        RuleId = RuleId,
        ServiceId = "vehicles",
        LayerId = 0,
        ZoneId = null,
        RuleName = "Speeding",
        TriggerType = "threshold",
        ConditionsJson = "{\"field\":\"speed\",\"operator\":\">\",\"value\":60}",
        CooldownSeconds = 300,
        Severity = "warning",
        EditionRequired = "pro",
        Channels = ["slack"],
        IsActive = true
    };

    private static AlertRuleHealthResponse BuildHealth() => new()
    {
        RuleId = RuleId,
        LastEvaluatedAt = DateTimeOffset.Parse("2026-06-03T11:58:00Z"),
        ActiveIncidentCount = 2,
        DeliveryFailureCount = 1,
        DeliveryChannels =
        [
            new AlertRuleDeliveryHealthResponse { Channel = "slack", Status = "configured" },
            new AlertRuleDeliveryHealthResponse { Channel = "email", Status = "failing", LastError = "SMTP 421" }
        ],
        RecentTriggers = []
    };

    private static AlertRuleTestResponse BuildValidation(bool isValid) => new()
    {
        IsValid = isValid,
        Errors = isValid ? [] : ["ZoneId is required for enter, exit, and dwell alert rules."],
        Warnings = [],
        DeliveryChannels =
        [
            new AlertChannelValidationResponse
            {
                Channel = "slack", Status = "configured", IsAllowed = true, IsConfigured = true,
                Message = "The 'slack' channel is available."
            }
        ],
        EvaluatedAt = DateTimeOffset.Parse("2026-06-03T12:00:00Z")
    };

    private static HttpResponseMessage Envelope<T>(
        T data,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<ConsoleApiEnvelope<T>> typeInfo)
    {
        var envelope = new ConsoleApiEnvelope<T> { Success = true, Data = data };
        var json = JsonSerializer.Serialize(envelope, typeInfo);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    /// <summary>The rule LIST path is not under test here; the editor get/save are.</summary>
    private sealed class StubObservabilityClient : IConsoleOperateObservabilityClient
    {
        public Task<OperateSectionResult<OperateFleetOverview>> GetOverviewAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<OperateFleetOverview>.Denied(OperateSectionStatus.Unavailable, "n/a"));

        public Task<OperateSectionResult<IReadOnlyList<OperateEventRow>>> QueryEventsAsync(OperateEventQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<IReadOnlyList<OperateEventRow>>.Denied(OperateSectionStatus.Unavailable, "n/a"));

        public Task<OperateSectionResult<OperateLogsView>> GetLogsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<OperateLogsView>.Denied(OperateSectionStatus.Unavailable, "n/a"));

        public Task<OperateSectionResult<IReadOnlyList<OperateAlertRecord>>> GetAlertsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<IReadOnlyList<OperateAlertRecord>>.Denied(OperateSectionStatus.Unavailable, "n/a"));

        public Task<OperateSectionResult<OperateRulesView>> GetRulesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<OperateRulesView>.Denied(OperateSectionStatus.Unavailable, "n/a"));

        public Task<OperateSectionResult<IReadOnlyList<OperateJobRun>>> GetJobsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<IReadOnlyList<OperateJobRun>>.Denied(OperateSectionStatus.Unavailable, "n/a"));

        public Task<OperateSectionResult<IReadOnlyList<OperateJobRun>>> GetJobsAsync(string? kind, CancellationToken cancellationToken = default) =>
            GetJobsAsync(cancellationToken);

        public Task<OperateSectionResult<IReadOnlyList<OperateJobRun>>> GetGeoprocessingJobsAsync(CancellationToken cancellationToken = default) =>
            GetJobsAsync(cancellationToken);

        public Task<OperateSectionResult<OperateJobRun>> GetJobDetailAsync(string jobRunId, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<OperateJobRun>.Denied(OperateSectionStatus.Unavailable, "n/a"));

        public Task<OperateSectionResult<OperateJobControlOutcome>> CancelJobAsync(string jobRunId, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<OperateJobControlOutcome>.Denied(OperateSectionStatus.Unavailable, "n/a"));

        public Task<OperateSectionResult<OperateJobControlOutcome>> RetryJobAsync(string jobRunId, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<OperateJobControlOutcome>.Denied(OperateSectionStatus.Unavailable, "n/a"));

        public Task<OperateSectionResult<IReadOnlyList<OperateInvestigation>>> GetInvestigationsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<IReadOnlyList<OperateInvestigation>>.Denied(OperateSectionStatus.Unavailable, "n/a"));

        public Task<OperateSectionResult<IReadOnlyList<OperateRecentError>>> GetRecentErrorsAsync(int limit = 10, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<IReadOnlyList<OperateRecentError>>.Denied(OperateSectionStatus.Unavailable, "n/a"));
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // The responder may read the content; buffer it first so the clone keeps it.
            var clone = CloneRequest(request);
            Requests.Add(clone);
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
