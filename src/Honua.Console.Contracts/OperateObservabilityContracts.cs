using System.Text.Json.Serialization;

namespace Honua.Console.Contracts;

// SHIM(honua-sdk-dotnet#231): The Operate observability admin contracts
// (events, logs, audit, alert lifecycle, alert rules, geofence zones, durable
// jobs, investigations) added by honua-server#1168, #1169, and #1170 are not
// yet projected to honua-sdk-dotnet. SDK 1.0.0 predates these endpoints and
// only exposes the narrow IHonuaAdminObservabilityClient. These records mirror
// the server HTTP/OpenAPI surface (NOT the server's internal protocol models)
// and are consumed through the single Console shim boundary until the SDK
// projection lands and honua-console swaps them for SDK types, exactly like the
// catalog shim in SdkShims.cs.
//
// Route map (concrete v1), all under /api/v{version:apiVersion}/admin:
//   GET  /version                           -> ApiResponse<AdminVersionResponse>
//   GET  /capabilities                      -> ApiResponse<AdminCapabilitiesResponse>
//   GET  /observability/errors              -> OperateRecentErrorsResponse
//   GET  /observability/telemetry           -> OperateTelemetryStatusResponse
//   GET  /observability/migrations          -> OperateMigrationStatusResponse
//   GET  /observability/events              -> OperateEventPageResponse
//   GET  /observability/logs                -> OperateLogPageResponse
//   GET  /observability/audit               -> ObservabilityAuditPageResponse
//   GET  /observability/alerts              -> ObservabilityAlertEventPageResponse
//   POST /observability/alerts/{id}/acknowledge|suppress|resolve
//   GET  /alerts/rules                       -> ApiResponse<AlertRuleResponse[]>
//   GET  /alerts/rules/{id}/health           -> ApiResponse<AlertRuleHealthResponse>
//   POST /alerts/rules/test                  -> ApiResponse<AlertRuleTestResponse>
//   GET  /alerts/zones                       -> ApiResponse<AlertZoneResponse[]>
//   GET  /jobs                               -> ConsoleJobListResponse
//   GET  /jobs/{id}                          -> ConsoleJobDetail
//   GET  /jobs/{id}/logs|artifacts|actions
//   GET  /investigations                     -> InvestigationPageResponse
//   GET  /investigations/{id}                -> InvestigationResponse
//
// JSON on the wire is camelCase; alert rules/zones additionally wrap payloads in
// ConsoleApiEnvelope<T> (success/data/message/timestamp).

public static class OperateAdminRoutes
{
    public const string Prefix = "api/v1/admin";

    public const string Version = Prefix + "/version";
    public const string Capabilities = Prefix + "/capabilities";
    public const string RecentErrors = Prefix + "/observability/errors";
    public const string Telemetry = Prefix + "/observability/telemetry";
    public const string Migrations = Prefix + "/observability/migrations";
    public const string Events = Prefix + "/observability/events";
    public const string Logs = Prefix + "/observability/logs";
    public const string Audit = Prefix + "/observability/audit";
    public const string Alerts = Prefix + "/observability/alerts";
    public const string AlertRules = Prefix + "/alerts/rules";
    public const string AlertZones = Prefix + "/alerts/zones";
    public const string Jobs = Prefix + "/jobs";
    public const string Investigations = Prefix + "/investigations";

    public static string AlertAcknowledge(long eventId) => $"{Alerts}/{eventId}/acknowledge";

    public static string AlertSuppress(long eventId) => $"{Alerts}/{eventId}/suppress";

    public static string AlertResolve(long eventId) => $"{Alerts}/{eventId}/resolve";

    public static string RuleHealth(long ruleId) => $"{AlertRules}/{ruleId}/health";

    public static string JobDetail(string jobId) => $"{Jobs}/{Uri.EscapeDataString(jobId)}";

    public static string JobLogs(string jobId) => $"{JobDetail(jobId)}/logs";

    public static string JobSteps(string jobId) => $"{JobDetail(jobId)}/steps";

    public static string JobArtifacts(string jobId) => $"{JobDetail(jobId)}/artifacts";

    public static string JobActions(string jobId) => $"{JobDetail(jobId)}/actions";

    public static string JobCancel(string jobId) => $"{JobDetail(jobId)}/cancel";

    public static string JobRetry(string jobId) => $"{JobDetail(jobId)}/retry";

    public static string InvestigationDetail(string investigationId) => $"{Investigations}/{Uri.EscapeDataString(investigationId)}";
}

/// <summary>
/// Builds the query string for the events/logs/jobs admin endpoints from
/// optional Console filters. Server-side filtering avoids loading the whole
/// timeline into memory and filtering client-side.
/// </summary>
public sealed record OperateEventQueryParameters
{
    public string Kind { get; init; } = string.Empty;

    public string MinSeverity { get; init; } = string.Empty;

    public string CorrelationId { get; init; } = string.Empty;

    public string TraceId { get; init; } = string.Empty;

    public string RequestId { get; init; } = string.Empty;

    public string ServiceId { get; init; } = string.Empty;

    public string ResourceRef { get; init; } = string.Empty;

    public string Actor { get; init; } = string.Empty;

    public string OperationId { get; init; } = string.Empty;

    public string ReleaseId { get; init; } = string.Empty;

    public string ChangeSetId { get; init; } = string.Empty;

    public string From { get; init; } = string.Empty;

    public string To { get; init; } = string.Empty;

    public int? PageSize { get; init; }

    public string ToQueryString()
    {
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
        Add(parameters, "kind", Kind);
        Add(parameters, "minSeverity", MinSeverity);
        Add(parameters, "correlationId", CorrelationId);
        Add(parameters, "traceId", TraceId);
        Add(parameters, "requestId", RequestId);
        Add(parameters, "serviceId", ServiceId);
        Add(parameters, "resourceRef", ResourceRef);
        Add(parameters, "actor", Actor);
        Add(parameters, "operationId", OperationId);
        Add(parameters, "releaseId", ReleaseId);
        Add(parameters, "changeSetId", ChangeSetId);
        Add(parameters, "from", From);
        Add(parameters, "to", To);
        if (PageSize is { } size && size > 0)
        {
            parameters["pageSize"] = size.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return ConsoleUrlQuery.ToQueryString(parameters);
    }

    private static void Add(IDictionary<string, string> parameters, string key, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            parameters[key] = value.Trim();
        }
    }
}

// --- Shared envelope (alert rules / geofence zones) ---------------------------

public sealed record ConsoleApiEnvelope<T>
{
    [JsonPropertyName("success")]
    public bool Success { get; init; }

    [JsonPropertyName("data")]
    public T? Data { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }
}

// --- Admin overview / telemetry -------------------------------------------------

public sealed record AdminVersionResponse
{
    public string Version { get; init; } = string.Empty;

    public string MetadataApiVersion { get; init; } = string.Empty;

    public string MetadataSchemaVersion { get; init; } = string.Empty;

    public DateTimeOffset ServerTime { get; init; }
}

public sealed record AdminCapabilitiesResponse
{
    public string MetadataApiVersion { get; init; } = string.Empty;

    public string MetadataSchemaVersion { get; init; } = string.Empty;

    public string ServerVersion { get; init; } = string.Empty;
}

public sealed record OperateRecentErrorsResponse
{
    public int Capacity { get; init; }

    public string InstanceId { get; init; } = string.Empty;

    public IReadOnlyList<OperateRecentErrorResponse> Errors { get; init; } = [];
}

public sealed record OperateRecentErrorResponse
{
    public DateTimeOffset Timestamp { get; init; }

    public string CorrelationId { get; init; } = string.Empty;

    public string Path { get; init; } = string.Empty;

    public int StatusCode { get; init; }

    public string Message { get; init; } = string.Empty;
}

public sealed record OperateTelemetryStatusResponse
{
    public DateTimeOffset GeneratedAt { get; init; }

    public OperateRealtimeStatusResponse Realtime { get; init; } = new();

    public bool TracingEnabled { get; init; }

    public bool MetricsEnabled { get; init; }

    public bool LogsEnabled { get; init; }

    public bool OtlpConfigured { get; init; }

    public bool OtlpEndpointValid { get; init; }

    public string? OtlpEndpoint { get; init; }

    public bool OtlpHeadersConfigured { get; init; }

    public string OtlpExporterState { get; init; } = "notConfigured";

    public string TraceExportState { get; init; } = "notConfigured";

    public string MetricsExportState { get; init; } = "notConfigured";

    public string LogExportState { get; init; } = "notConfigured";

    public string? LastExportError { get; init; }
}

public sealed record OperateRealtimeStatusResponse
{
    public bool Supported { get; init; }

    public string? HubPath { get; init; }

    public string? Protocol { get; init; }

    public string[] Events { get; init; } = [];
}

public sealed record OperateMigrationStatusResponse
{
    public string Status { get; init; } = string.Empty;

    public bool IsReady { get; init; }

    public bool IsFailed { get; init; }

    public string? Message { get; init; }

    public bool PlanAvailable { get; init; }

    public bool UpgradeRequired { get; init; }

    public IReadOnlyList<string> PendingScripts { get; init; } = [];

    public IReadOnlyList<string> ExecutedButNotDiscoveredScripts { get; init; } = [];

    public string? PlanError { get; init; }

    public DateTimeOffset GeneratedAt { get; init; }
}

// --- Events / logs / audit ----------------------------------------------------

public sealed record OperateEventPageResponse
{
    public IReadOnlyList<OperateEventResponse> Items { get; init; } = [];

    public bool PartialResult { get; init; }

    public IReadOnlyDictionary<string, string>? SourceErrors { get; init; }
}

public sealed record OperateEventResponse
{
    public string EventId { get; init; } = string.Empty;

    public string Kind { get; init; } = string.Empty;

    public string Severity { get; init; } = string.Empty;

    public DateTimeOffset OccurredAt { get; init; }

    public string Title { get; init; } = string.Empty;

    public string? Summary { get; init; }

    public string? ServiceId { get; init; }

    public int? LayerId { get; init; }

    public long? ObjectId { get; init; }

    public string? Actor { get; init; }

    public string? CorrelationId { get; init; }

    public string? TraceId { get; init; }

    public string? RequestId { get; init; }

    public string? OperationId { get; init; }

    public string? ReleaseId { get; init; }

    public string? ReplicaId { get; init; }

    public string? ChangeSetId { get; init; }

    public string? ResourceRef { get; init; }

    public IReadOnlyList<OperateProviderLinkResponse>? ProviderLinks { get; init; }

    public string? DetailsJson { get; init; }
}

public sealed record OperateProviderLinkResponse
{
    public string Provider { get; init; } = string.Empty;

    public string Label { get; init; } = string.Empty;

    public string Url { get; init; } = string.Empty;
}

public sealed record OperateLogPageResponse
{
    public string InstanceId { get; init; } = string.Empty;

    public int Capacity { get; init; }

    public IReadOnlyList<OperateLogEntryResponse> Items { get; init; } = [];
}

public sealed record OperateLogEntryResponse
{
    public DateTimeOffset Timestamp { get; init; }

    public string Level { get; init; } = string.Empty;

    public string? Path { get; init; }

    public int StatusCode { get; init; }

    public string CorrelationId { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;
}

public sealed record ObservabilityAuditPageResponse
{
    public IReadOnlyList<ObservabilityAuditRecordResponse> Items { get; init; } = [];

    public string? NextCursor { get; init; }
}

public sealed record ObservabilityAuditRecordResponse
{
    public long AuditId { get; init; }

    public DateTimeOffset Timestamp { get; init; }

    public string EventType { get; init; } = string.Empty;

    public string Actor { get; init; } = string.Empty;

    public string ActorType { get; init; } = string.Empty;

    public string ResourceType { get; init; } = string.Empty;

    public string? ResourceId { get; init; }

    public string Action { get; init; } = string.Empty;

    public string Outcome { get; init; } = string.Empty;

    public string CorrelationId { get; init; } = string.Empty;

    public string Details { get; init; } = string.Empty;
}

// --- Alerts (geofence / realtime alert events) --------------------------------

public sealed record ObservabilityAlertEventPageResponse
{
    public IReadOnlyList<ObservabilityAlertEventResponse> Items { get; init; } = [];

    public string? NextCursor { get; init; }
}

public sealed record ObservabilityAlertEventResponse
{
    public long EventId { get; init; }

    public long RuleId { get; init; }

    public string? RuleName { get; init; }

    public long? ZoneId { get; init; }

    public string ServiceId { get; init; } = string.Empty;

    public int LayerId { get; init; }

    public long ObjectId { get; init; }

    public string TriggerType { get; init; } = string.Empty;

    public string Severity { get; init; } = string.Empty;

    public DateTimeOffset OccurredAt { get; init; }

    public string IncidentStatus { get; init; } = string.Empty;

    public long IncidentDurationMs { get; init; }

    public string LifecycleStatus { get; init; } = string.Empty;

    public DateTimeOffset? AcknowledgedAt { get; init; }

    public string? AcknowledgedBy { get; init; }

    public DateTimeOffset? SuppressedUntil { get; init; }

    public DateTimeOffset? ResolvedAt { get; init; }

    public string? ResolvedBy { get; init; }

    public string ResourceRef { get; init; } = string.Empty;
}

// --- Alert rules + health + test ----------------------------------------------

public sealed record AlertRuleResponse
{
    public long RuleId { get; init; }

    public string ServiceId { get; init; } = string.Empty;

    public int LayerId { get; init; }

    public long? ZoneId { get; init; }

    public string RuleName { get; init; } = string.Empty;

    public string TriggerType { get; init; } = string.Empty;

    public string ConditionsJson { get; init; } = "{}";

    public int CooldownSeconds { get; init; }

    public string Severity { get; init; } = string.Empty;

    public string EditionRequired { get; init; } = string.Empty;

    public string[] Channels { get; init; } = [];

    public bool IsActive { get; init; }
}

public sealed record AlertRuleHealthResponse
{
    public long RuleId { get; init; }

    public DateTimeOffset? LastEvaluatedAt { get; init; }

    public DateTimeOffset? LastTriggeredAt { get; init; }

    public int ActiveIncidentCount { get; init; }

    public int RecentTriggerCount { get; init; }

    public int CoolingDownFeatureCount { get; init; }

    public DateTimeOffset? NextCooldownExpiresAt { get; init; }

    public int DeliveryFailureCount { get; init; }

    public int DeadLetterCount { get; init; }

    public long[] LinkedEventIds { get; init; } = [];

    public AlertRuleDeliveryHealthResponse[] DeliveryChannels { get; init; } = [];

    public AlertRuleRecentTriggerResponse[] RecentTriggers { get; init; } = [];
}

public sealed record AlertRuleDeliveryHealthResponse
{
    public string Channel { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public int PendingCount { get; init; }

    public int ProcessingCount { get; init; }

    public int DeliveredCount { get; init; }

    public int FailedCount { get; init; }

    public int DeadLetterCount { get; init; }

    public DateTimeOffset? LastAttemptAt { get; init; }

    public DateTimeOffset? LastDeliveredAt { get; init; }

    public string? LastError { get; init; }
}

public sealed record AlertRuleRecentTriggerResponse
{
    public long EventId { get; init; }

    public string TriggerType { get; init; } = string.Empty;

    public string Severity { get; init; } = string.Empty;

    public DateTimeOffset OccurredAt { get; init; }

    public string IncidentStatus { get; init; } = string.Empty;

    public string LifecycleStatus { get; init; } = string.Empty;

    public string ResourceRef { get; init; } = string.Empty;
}

public sealed record AlertRuleTestResponse
{
    public bool IsValid { get; init; }

    public string[] Errors { get; init; } = [];

    public string[] Warnings { get; init; } = [];

    public AlertChannelValidationResponse[] DeliveryChannels { get; init; } = [];

    public DateTimeOffset EvaluatedAt { get; init; }
}

public sealed record AlertChannelValidationResponse
{
    public string Channel { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public bool IsAllowed { get; init; }

    public bool IsConfigured { get; init; }

    public string Message { get; init; } = string.Empty;
}

// --- Geofence zones -----------------------------------------------------------

public sealed record AlertZoneResponse
{
    public long ZoneId { get; init; }

    public string ServiceId { get; init; } = string.Empty;

    public string ZoneName { get; init; } = string.Empty;

    public string? Wkt { get; init; }

    public int? Srid { get; init; }

    public Dictionary<string, string?> Metadata { get; init; } = new(StringComparer.Ordinal);

    public bool IsActive { get; init; }
}

// --- Durable jobs -------------------------------------------------------------

public sealed record ConsoleJobListResponse
{
    public ConsoleJobSummary[] Items { get; init; } = [];

    public string? NextCursor { get; init; }
}

public record ConsoleJobSummary
{
    public string JobId { get; init; } = string.Empty;

    public string Kind { get; init; } = string.Empty;

    public string? Queue { get; init; }

    public string Backend { get; init; } = string.Empty;

    public string TargetKind { get; init; } = string.Empty;

    public string WorkloadName { get; init; } = string.Empty;

    public string? DefinitionId { get; init; }

    public string Status { get; init; } = string.Empty;

    public string Priority { get; init; } = string.Empty;

    public string? RequestedBy { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }

    public long? DurationMs { get; init; }

    public double? PercentComplete { get; init; }

    public string? CurrentPhase { get; init; }

    public int AttemptCount { get; init; }

    public int MaxAttempts { get; init; }

    public string? CorrelationId { get; init; }

    public string? TraceId { get; init; }

    public string[] ResourceRefs { get; init; } = [];

    public int ArtifactCount { get; init; }

    public ConsoleJobLatestEvent? LatestEvent { get; init; }

    public ConsoleJobLinks? Links { get; init; }
}

public sealed record ConsoleJobDetail : ConsoleJobSummary
{
    public string? ProviderOperationId { get; init; }

    public string? ClaimedBy { get; init; }

    public string[] Warnings { get; init; } = [];

    public ConsoleJobFailure? Failure { get; init; }

    public ConsoleJobRetryPolicy? RetryPolicy { get; init; }

    public string? ParentJobId { get; init; }

    public string[] ChildJobIds { get; init; } = [];

    public Dictionary<string, string> SelectedMetadata { get; init; } = new(StringComparer.Ordinal);

    public ConsoleJobStage[] Stages { get; init; } = [];

    public ConsoleJobActionDescriptor[] Actions { get; init; } = [];
}

public sealed record ConsoleJobLatestEvent
{
    public string Type { get; init; } = string.Empty;

    public DateTimeOffset OccurredAt { get; init; }

    public string Status { get; init; } = string.Empty;

    public string? Phase { get; init; }

    public string? Message { get; init; }
}

public sealed record ConsoleJobLinks
{
    public string Self { get; init; } = string.Empty;

    public string Logs { get; init; } = string.Empty;

    public string Artifacts { get; init; } = string.Empty;

    public string Actions { get; init; } = string.Empty;

    public string? Cancel { get; init; }

    public string? Retry { get; init; }

    public string? EventsByJob { get; init; }

    public string? EventsByCorrelation { get; init; }
}

public sealed record ConsoleJobFailure
{
    public string Message { get; init; } = string.Empty;

    public string Classification { get; init; } = string.Empty;
}

public sealed record ConsoleJobRetryPolicy
{
    public int MaxAttempts { get; init; }

    public string Strategy { get; init; } = string.Empty;

    public long BaseDelayMs { get; init; }

    public long MaxDelayMs { get; init; }
}

public sealed record ConsoleJobStage
{
    public string Name { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public DateTimeOffset? StartedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }

    public double? PercentComplete { get; init; }
}

public sealed record ConsoleJobActionDescriptor
{
    public string Name { get; init; } = string.Empty;

    public string Method { get; init; } = string.Empty;

    public string Href { get; init; } = string.Empty;

    public bool Allowed { get; init; }

    public string? DisabledReason { get; init; }

    public bool RequiresApproval { get; init; }
}

public sealed record ConsoleJobLogPageResponse
{
    public string JobId { get; init; } = string.Empty;

    public string? CorrelationId { get; init; }

    public string State { get; init; } = string.Empty;

    public ConsoleJobLogEntry[] Items { get; init; } = [];

    public string? NextCursor { get; init; }
}

public sealed record ConsoleJobLogEntry
{
    public DateTimeOffset Timestamp { get; init; }

    public string Level { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public string? Phase { get; init; }
}

public sealed record ConsoleJobArtifactPageResponse
{
    public string JobId { get; init; } = string.Empty;

    public ConsoleJobArtifact[] Items { get; init; } = [];

    public string? NextCursor { get; init; }
}

public sealed record ConsoleJobArtifact
{
    public string ArtifactId { get; init; } = string.Empty;

    public string Availability { get; init; } = string.Empty;

    public string? Kind { get; init; }

    public string? Label { get; init; }

    public string? ContentType { get; init; }

    public long? SizeBytes { get; init; }

    public string? ProviderLink { get; init; }

    public string? Message { get; init; }
}

public sealed record ConsoleJobActionsResponse
{
    public string JobId { get; init; } = string.Empty;

    public ConsoleJobActionDescriptor[] Actions { get; init; } = [];
}

/// <summary>
/// Result of a durable-job control action (<c>POST /jobs/{id}/cancel</c> or
/// <c>POST /jobs/{id}/retry</c>). The server returns the action that ran, the
/// job's resulting status, and an operator-facing message. The server's
/// Execute + destructive-approval gate is the authority: a denied action returns
/// 403 (forbidden / approval-required) rather than this envelope.
/// </summary>
public sealed record ConsoleJobControlResponse
{
    public string JobId { get; init; } = string.Empty;

    public string? CorrelationId { get; init; }

    public string Action { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;
}

/// <summary>
/// The sanitized per-step glass-box for a durable execution job
/// (<c>GET /api/v1/admin/jobs/{id}/steps</c>, honua-server #2182). Each step is a
/// projection of one durable execution-log entry: its phase, timeline, status,
/// the sanitized provider command (e.g. the GDAL command with
/// <c>&lt;scratch&gt;</c>/<c>&lt;path&gt;</c> redacted server-side), an artifact
/// summary, and metadata. The server has already sanitized <see cref="ConsoleJobStep.Command"/>;
/// the Console renders it verbatim and must not re-sanitize it.
/// </summary>
public sealed record ConsoleJobStepsResponse
{
    public string JobId { get; init; } = string.Empty;

    public string? CorrelationId { get; init; }

    public string State { get; init; } = string.Empty;

    public ConsoleJobStep[] Steps { get; init; } = [];
}

public sealed record ConsoleJobStep
{
    public int Ordinal { get; init; }

    public string Phase { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public DateTimeOffset StartedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }

    public long? DurationMs { get; init; }

    public string Message { get; init; } = string.Empty;

    public string? Command { get; init; }

    public ConsoleJobStepArtifact[]? Artifacts { get; init; }

    public Dictionary<string, string>? Metadata { get; init; }
}

public sealed record ConsoleJobStepArtifact
{
    public string Label { get; init; } = string.Empty;

    public string? Kind { get; init; }

    public long? SizeBytes { get; init; }
}

// --- Investigations -----------------------------------------------------------

public sealed record InvestigationPageResponse
{
    public IReadOnlyList<InvestigationSummaryResponse> Items { get; init; } = [];

    public string? NextCursor { get; init; }
}

public sealed record InvestigationSummaryResponse
{
    public string InvestigationId { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public DateTimeOffset CreatedAt { get; init; }

    public string CreatedBy { get; init; } = string.Empty;

    public DateTimeOffset UpdatedAt { get; init; }

    public string? Summary { get; init; }

    public int PinCount { get; init; }

    public int LinkCount { get; init; }
}

public sealed record InvestigationResponse
{
    public string InvestigationId { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public DateTimeOffset CreatedAt { get; init; }

    public string CreatedBy { get; init; } = string.Empty;

    public DateTimeOffset UpdatedAt { get; init; }

    public string? Summary { get; init; }

    public IReadOnlyList<InvestigationPinResponse> Pins { get; init; } = [];

    public IReadOnlyList<InvestigationLinkResponse> Links { get; init; } = [];
}

public sealed record InvestigationPinResponse
{
    public long PinId { get; init; }

    public string EventRef { get; init; } = string.Empty;

    public string EventKind { get; init; } = string.Empty;

    public DateTimeOffset OccurredAt { get; init; }

    public string? Note { get; init; }
}

public sealed record InvestigationLinkResponse
{
    public long LinkId { get; init; }

    public string ResourceKind { get; init; } = string.Empty;

    public string ResourceId { get; init; } = string.Empty;

    public string? Note { get; init; }
}

/// <summary>
/// Source-generated serialization context for the Operate observability admin
/// contracts. Source generation keeps the surface trim/AOT-safe (no
/// reflection-based serialization), per the Console runtime constraints.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ConsoleApiEnvelope<AdminVersionResponse>), TypeInfoPropertyName = "AdminVersionEnvelope")]
[JsonSerializable(typeof(ConsoleApiEnvelope<AdminCapabilitiesResponse>), TypeInfoPropertyName = "AdminCapabilitiesEnvelope")]
[JsonSerializable(typeof(OperateRecentErrorsResponse))]
[JsonSerializable(typeof(OperateTelemetryStatusResponse))]
[JsonSerializable(typeof(OperateMigrationStatusResponse))]
[JsonSerializable(typeof(OperateEventPageResponse))]
[JsonSerializable(typeof(OperateLogPageResponse))]
[JsonSerializable(typeof(ObservabilityAuditPageResponse))]
[JsonSerializable(typeof(ObservabilityAlertEventPageResponse))]
[JsonSerializable(typeof(ConsoleApiEnvelope<AlertRuleResponse[]>), TypeInfoPropertyName = "AlertRuleListEnvelope")]
[JsonSerializable(typeof(ConsoleApiEnvelope<AlertRuleResponse>), TypeInfoPropertyName = "AlertRuleEnvelope")]
[JsonSerializable(typeof(ConsoleApiEnvelope<AlertRuleHealthResponse>), TypeInfoPropertyName = "AlertRuleHealthEnvelope")]
[JsonSerializable(typeof(ConsoleApiEnvelope<AlertRuleTestResponse>), TypeInfoPropertyName = "AlertRuleTestEnvelope")]
[JsonSerializable(typeof(ConsoleApiEnvelope<AlertZoneResponse[]>), TypeInfoPropertyName = "AlertZoneListEnvelope")]
[JsonSerializable(typeof(ConsoleApiEnvelope<AlertZoneResponse>), TypeInfoPropertyName = "AlertZoneEnvelope")]
[JsonSerializable(typeof(ConsoleJobListResponse))]
[JsonSerializable(typeof(ConsoleJobDetail))]
[JsonSerializable(typeof(ConsoleJobSummary))]
[JsonSerializable(typeof(ConsoleJobLogPageResponse))]
[JsonSerializable(typeof(ConsoleJobArtifactPageResponse))]
[JsonSerializable(typeof(ConsoleJobActionsResponse))]
[JsonSerializable(typeof(ConsoleJobControlResponse))]
[JsonSerializable(typeof(ConsoleJobStepsResponse))]
[JsonSerializable(typeof(InvestigationPageResponse))]
[JsonSerializable(typeof(InvestigationResponse))]
public sealed partial class OperateObservabilityJsonContext : JsonSerializerContext;
