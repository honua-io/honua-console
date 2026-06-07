using System.Text.Json.Serialization;

namespace Honua.Console.Contracts;

// SHIM(honua-sdk-dotnet#231): The alert-rule ADMIN authoring contract shipped by
// honua-server#1169 (rule get/create/update/test/health under
// /api/v{version}/admin/alerts) is not yet projected to honua-sdk-dotnet. The
// READ response shapes (AlertRuleResponse, AlertRuleHealthResponse,
// AlertRuleTestResponse, AlertChannelValidationResponse) plus the shared
// ConsoleApiEnvelope<T> already live in OperateObservabilityContracts.cs; this
// file adds the WRITE/test REQUEST shapes (AlertRuleRequest, AlertRuleTestRequest)
// and the concrete admin-alerts route map so the rule editor's get/save/validate
// path binds the same /api/v{version}/admin/alerts surface the rule LIST already
// reads. These mirror the server HTTP/OpenAPI surface (NOT the server's internal
// domain models) and are consumed through the single Console shim boundary until
// the SDK projection lands.
//
// Route map (concrete v1), all under /api/v1/admin/alerts, admin-authorized
// (X-API-Key), JSON camelCase, ApiResponse<T> (ConsoleApiEnvelope<T>) envelope:
//   GET  /rules/{ruleId}        -> ConsoleApiEnvelope<AlertRuleResponse>
//   POST /rules                 (AlertRuleRequest) -> ConsoleApiEnvelope<AlertRuleResponse>   (create)
//   PUT  /rules/{ruleId}        (AlertRuleRequest) -> ConsoleApiEnvelope<AlertRuleResponse>   (update)
//   POST /rules/test            (AlertRuleTestRequest) -> ConsoleApiEnvelope<AlertRuleTestResponse>
//   GET  /rules/{ruleId}/health -> ConsoleApiEnvelope<AlertRuleHealthResponse>

/// <summary>
/// Concrete v1 routes for the alert-rule admin authoring contract, kept in one
/// place so the client and tests share the exact server paths.
/// </summary>
public static class AlertAdminRoutes
{
    public const string Prefix = "api/v1/admin/alerts";

    public const string Rules = Prefix + "/rules";

    public const string RulesTest = Rules + "/test";

    public static string Rule(long ruleId) => $"{Rules}/{ruleId}";

    public static string RuleHealth(long ruleId) => $"{Rule(ruleId)}/health";
}

/// <summary>
/// Rule create/update request payload. Mirrors the server <c>AlertRuleRequest</c>
/// wire shape (camelCase). <c>conditionsJson</c> is a JSON STRING whose shape is
/// trigger-specific (threshold: <c>{field,operator,value}</c>; dwell:
/// <c>{dwellSeconds}</c>; enter/exit: no required conditions).
/// </summary>
public sealed record AlertRuleRequest
{
    [JsonPropertyName("serviceId")]
    public string ServiceId { get; init; } = string.Empty;

    [JsonPropertyName("layerId")]
    public int LayerId { get; init; }

    [JsonPropertyName("zoneId")]
    public long? ZoneId { get; init; }

    [JsonPropertyName("ruleName")]
    public string RuleName { get; init; } = string.Empty;

    [JsonPropertyName("triggerType")]
    public string TriggerType { get; init; } = string.Empty;

    [JsonPropertyName("conditionsJson")]
    public string ConditionsJson { get; init; } = "{}";

    [JsonPropertyName("cooldownSeconds")]
    public int CooldownSeconds { get; init; }

    [JsonPropertyName("severity")]
    public string Severity { get; init; } = "warning";

    [JsonPropertyName("editionRequired")]
    public string EditionRequired { get; init; } = "pro";

    [JsonPropertyName("channels")]
    public string[] Channels { get; init; } = [];

    [JsonPropertyName("isActive")]
    public bool IsActive { get; init; } = true;
}

/// <summary>
/// Draft-rule validation request. The optional <c>zone</c> lets the editor
/// validate a brand-new geofence draft with the rule before either is persisted;
/// the console rule editor does not author zones, so it is left null here.
/// </summary>
public sealed record AlertRuleTestRequest
{
    [JsonPropertyName("rule")]
    public AlertRuleRequest Rule { get; init; } = new();
}

/// <summary>
/// Threshold trigger conditions payload (the parsed shape of <c>conditionsJson</c>
/// for a <c>threshold</c> rule): a metric field, a comparison operator, and a
/// numeric value.
/// </summary>
public sealed record AlertThresholdConditions
{
    [JsonPropertyName("field")]
    public string? Field { get; init; }

    [JsonPropertyName("operator")]
    public string? Operator { get; init; }

    [JsonPropertyName("value")]
    public double? Value { get; init; }
}

/// <summary>
/// Dwell trigger conditions payload (the parsed shape of <c>conditionsJson</c> for
/// a <c>dwell</c> rule): the minimum dwell duration in seconds.
/// </summary>
public sealed record AlertDwellConditions
{
    [JsonPropertyName("dwellSeconds")]
    public int? DwellSeconds { get; init; }
}

/// <summary>
/// Source-generated JSON context for the alert-rule admin write/test request
/// shapes and the trigger-specific <c>conditionsJson</c> payloads (trim/AOT safe),
/// mirroring OperateObservabilityJsonContext. Response envelopes are source-genned
/// in OperateObservabilityJsonContext.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(AlertRuleRequest))]
[JsonSerializable(typeof(AlertRuleTestRequest))]
[JsonSerializable(typeof(AlertThresholdConditions))]
[JsonSerializable(typeof(AlertDwellConditions))]
public sealed partial class AlertAdminJsonContext : JsonSerializerContext
{
}
