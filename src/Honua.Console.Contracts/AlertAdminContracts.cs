using System.Text.Json.Serialization;

namespace Honua.Console.Contracts;

public static class AlertAdminRoutes
{
    public const string Prefix = "api/v1/admin/alerts";

    public const string Rules = Prefix + "/rules";

    public const string RulesTest = Rules + "/test";

    public static string Rule(long ruleId) => $"{Rules}/{ruleId}";

    public static string RuleHealth(long ruleId) => $"{Rule(ruleId)}/health";

    public static string RuleEnabled(long ruleId) => $"{Rule(ruleId)}/enabled";
}

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

    // This is a JSON string, not an object, and the inner shape depends on the trigger.
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

// Zone is left unset because the console does not author geofence zones.
public sealed record AlertRuleTestRequest
{
    [JsonPropertyName("rule")]
    public AlertRuleRequest Rule { get; init; } = new();
}

// A test run never changes this flag; enable and disable are their own command.
public sealed record AlertRuleEnabledRequest
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; }
}

public sealed record AlertThresholdConditions
{
    [JsonPropertyName("field")]
    public string? Field { get; init; }

    [JsonPropertyName("operator")]
    public string? Operator { get; init; }

    [JsonPropertyName("value")]
    public double? Value { get; init; }
}

public sealed record AlertDwellConditions
{
    [JsonPropertyName("dwellSeconds")]
    public int? DwellSeconds { get; init; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(AlertRuleRequest))]
[JsonSerializable(typeof(AlertRuleTestRequest))]
[JsonSerializable(typeof(AlertRuleEnabledRequest))]
[JsonSerializable(typeof(AlertThresholdConditions))]
[JsonSerializable(typeof(AlertDwellConditions))]
public sealed partial class AlertAdminJsonContext : JsonSerializerContext
{
}
