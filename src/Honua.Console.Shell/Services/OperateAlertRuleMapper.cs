using System.Globalization;
using System.Text.Json;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Pure projection between the alert-rule admin wire contracts
/// (<see cref="AlertRuleResponse"/>/<see cref="AlertRuleHealthResponse"/>/
/// <see cref="AlertRuleRequest"/>, honua-server#1169) and the Console rule editor
/// models (<see cref="OperateAlertRuleDefinition"/>/<see cref="OperateAlertRuleCondition"/>/
/// <see cref="OperateAlertRuleEdit"/>).
///
/// The tricky part is the condition mapping: the server carries a
/// <c>triggerType</c> ("enter"|"exit"|"dwell"|"threshold"), a <c>zoneId</c>, and a
/// <c>conditionsJson</c> STRING; the editor binds a flat
/// <see cref="OperateAlertRuleCondition"/>. <see cref="MapCondition"/> reads the
/// trigger + conditionsJson into the flat condition;
/// <see cref="TryBuildConditionsJson"/> does the inverse and fails honestly
/// (returns false) when an edited condition cannot be represented faithfully,
/// rather than guessing.
/// </summary>
public static class OperateAlertRuleMapper
{
    // --- Wire -> Console -----------------------------------------------------

    /// <summary>Projects a server rule (+ optional health) into the editor definition.</summary>
    public static OperateAlertRuleDefinition MapDefinition(
        AlertRuleResponse rule,
        AlertRuleHealthResponse? health,
        AlertRuleTestResponse? validation)
    {
        var condition = MapCondition(rule);
        var channels = (rule.Channels ?? []).ToArray();
        var validationMessages = BuildValidationMessages(rule, health, validation);

        return new OperateAlertRuleDefinition(
            RuleId: rule.RuleId.ToString(CultureInfo.InvariantCulture),
            Name: rule.RuleName,
            RuleType: rule.ZoneId is null
                ? NormalizeToken(rule.TriggerType)
                : $"geofence:{NormalizeToken(rule.TriggerType)}",
            Enabled: rule.IsActive,
            Status: ResolveStatus(rule, health, validation, validationMessages.Count > 0),
            Description: BuildDescription(rule),
            Condition: condition,
            DeliverySummary: channels.Length == 0 ? "no channel configured" : string.Join(", ", channels),
            DeliveryChannels: channels,
            LastEvaluatedAt: FormatTimestamp(health?.LastEvaluatedAt),
            ActiveIncidentCount: health?.ActiveIncidentCount ?? 0,
            DeliveryFailureCount: health?.DeliveryFailureCount ?? 0,
            ValidationMessages: validationMessages);
    }

    /// <summary>
    /// Reads the server <c>triggerType</c> + <c>conditionsJson</c> + <c>zoneId</c>
    /// into the flat editor condition. threshold.field-&gt;Subject,
    /// operator-&gt;Operator, value-&gt;Threshold; dwell.dwellSeconds-&gt;DwellMinutes
    /// (seconds-&gt;minutes); zoneId-&gt;GeofenceZoneId.
    /// </summary>
    public static OperateAlertRuleCondition MapCondition(AlertRuleResponse rule)
    {
        var trigger = NormalizeToken(rule.TriggerType);
        var zoneId = rule.ZoneId?.ToString(CultureInfo.InvariantCulture);
        var conditionsJson = string.IsNullOrWhiteSpace(rule.ConditionsJson) ? "{}" : rule.ConditionsJson;

        if (string.Equals(trigger, "threshold", StringComparison.Ordinal))
        {
            var threshold = TryParse(conditionsJson, AlertAdminJsonContext.Default.AlertThresholdConditions);
            return new OperateAlertRuleCondition(
                Subject: threshold?.Field ?? string.Empty,
                Operator: threshold?.Operator ?? string.Empty,
                Threshold: threshold?.Value is { } value
                    ? value.ToString(CultureInfo.InvariantCulture)
                    : string.Empty,
                Window: string.Empty,
                GeofenceZoneId: null,
                DwellMinutes: null);
        }

        if (string.Equals(trigger, "dwell", StringComparison.Ordinal))
        {
            var dwell = TryParse(conditionsJson, AlertAdminJsonContext.Default.AlertDwellConditions);
            return new OperateAlertRuleCondition(
                Subject: $"zone:{trigger}",
                Operator: trigger,
                Threshold: string.Empty,
                Window: string.Empty,
                GeofenceZoneId: zoneId,
                DwellMinutes: SecondsToMinutes(dwell?.DwellSeconds));
        }

        // enter / exit: the zone transition is the trigger; no required conditions.
        return new OperateAlertRuleCondition(
            Subject: $"zone:{trigger}",
            Operator: trigger,
            Threshold: string.Empty,
            Window: string.Empty,
            GeofenceZoneId: zoneId,
            DwellMinutes: null);
    }

    // --- Console -> Wire -----------------------------------------------------

    /// <summary>
    /// Builds the create/update request for a save: the editor-owned fields
    /// (name, enablement, condition, channels) overlay the immutable fields read
    /// from the persisted <paramref name="current"/> rule (service/layer/cooldown/
    /// severity/edition), so an UPDATE preserves what the editor does not author.
    /// Returns false with an honest error when the edited condition cannot be
    /// represented on the wire (Console Patterns Charter section 11).
    /// </summary>
    public static bool TryBuildRequest(
        OperateAlertRuleEdit edit,
        AlertRuleResponse current,
        out AlertRuleRequest request,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(edit);
        ArgumentNullException.ThrowIfNull(current);

        if (!TryBuildConditionsJson(edit.Condition, out var triggerType, out var zoneId, out var conditionsJson, out error))
        {
            request = new AlertRuleRequest();
            return false;
        }

        request = new AlertRuleRequest
        {
            ServiceId = current.ServiceId,
            LayerId = current.LayerId,
            ZoneId = zoneId,
            RuleName = edit.Name,
            TriggerType = triggerType,
            ConditionsJson = conditionsJson,
            CooldownSeconds = current.CooldownSeconds,
            Severity = string.IsNullOrWhiteSpace(current.Severity) ? "warning" : current.Severity,
            EditionRequired = string.IsNullOrWhiteSpace(current.EditionRequired) ? "pro" : current.EditionRequired,
            Channels = (edit.DeliveryChannels ?? []).ToArray(),
            IsActive = edit.Enabled
        };
        error = string.Empty;
        return true;
    }

    /// <summary>
    /// Inverse of <see cref="MapCondition"/>: derives the wire trigger type, zone
    /// id, and conditionsJson string from the editor condition. The trigger is read
    /// from the condition's Operator (which <see cref="MapCondition"/> populates for
    /// the geofence triggers) or inferred as "threshold" when a metric subject +
    /// operator + numeric threshold are present.
    /// </summary>
    public static bool TryBuildConditionsJson(
        OperateAlertRuleCondition condition,
        out string triggerType,
        out long? zoneId,
        out string conditionsJson,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(condition);

        triggerType = ResolveTriggerType(condition);
        zoneId = null;
        conditionsJson = "{}";
        error = string.Empty;

        switch (triggerType)
        {
            case "threshold":
                if (string.IsNullOrWhiteSpace(condition.Subject)
                    || string.IsNullOrWhiteSpace(condition.Operator))
                {
                    error = "A threshold rule requires a metric field and a comparison operator.";
                    return false;
                }

                if (!double.TryParse(condition.Threshold, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                {
                    error = "A threshold rule requires a numeric threshold value.";
                    return false;
                }

                conditionsJson = JsonSerializer.Serialize(
                    new AlertThresholdConditions
                    {
                        Field = condition.Subject.Trim(),
                        Operator = condition.Operator.Trim(),
                        Value = value
                    },
                    AlertAdminJsonContext.Default.AlertThresholdConditions);
                return true;

            case "dwell":
                if (!TryParseZoneId(condition.GeofenceZoneId, out zoneId))
                {
                    error = "A dwell rule requires a valid geofence zone.";
                    return false;
                }

                if (condition.DwellMinutes is not { } minutes || minutes <= 0)
                {
                    error = "A dwell rule requires a positive dwell duration.";
                    return false;
                }

                conditionsJson = JsonSerializer.Serialize(
                    new AlertDwellConditions { DwellSeconds = checked(minutes * 60) },
                    AlertAdminJsonContext.Default.AlertDwellConditions);
                return true;

            case "enter":
            case "exit":
                if (!TryParseZoneId(condition.GeofenceZoneId, out zoneId))
                {
                    error = $"An {triggerType} rule requires a valid geofence zone.";
                    return false;
                }

                conditionsJson = "{}";
                return true;

            default:
                error = $"Unsupported alert trigger type '{triggerType}'.";
                return false;
        }
    }

    // --- Helpers -------------------------------------------------------------

    private static string ResolveTriggerType(OperateAlertRuleCondition condition)
    {
        var op = NormalizeToken(condition.Operator);
        if (op is "enter" or "exit" or "dwell" or "threshold")
        {
            return op;
        }

        // A geofence subject + dwell minutes implies dwell; a zone alone with no
        // dwell implies a transition (enter); otherwise a metric threshold.
        if (condition.DwellMinutes is { } minutes && minutes > 0)
        {
            return "dwell";
        }

        if (!string.IsNullOrWhiteSpace(condition.GeofenceZoneId)
            && string.IsNullOrWhiteSpace(condition.Threshold))
        {
            return "enter";
        }

        return "threshold";
    }

    private static IReadOnlyList<string> BuildValidationMessages(
        AlertRuleResponse rule,
        AlertRuleHealthResponse? health,
        AlertRuleTestResponse? validation)
    {
        var messages = new List<string>();

        if (validation is not null)
        {
            messages.AddRange((validation.Errors ?? []).Where(message => !string.IsNullOrWhiteSpace(message)));
            messages.AddRange((validation.Warnings ?? []).Where(message => !string.IsNullOrWhiteSpace(message)));
            foreach (var channel in validation.DeliveryChannels ?? [])
            {
                if (!channel.IsAllowed || !channel.IsConfigured || IsFailureStatus(channel.Status))
                {
                    messages.Add($"Channel {channel.Channel}: {ChannelDetail(channel)}");
                }
            }
        }

        if ((rule.Channels ?? []).Length == 0)
        {
            messages.Add("Configure at least one delivery channel.");
        }

        if (health is not null)
        {
            foreach (var channel in (health.DeliveryChannels ?? [])
                .Where(channel => IsFailureStatus(channel.Status)
                    || string.Equals(NormalizeToken(channel.Status), "unconfigured", StringComparison.Ordinal)))
            {
                var detail = string.IsNullOrWhiteSpace(channel.LastError) ? channel.Status : channel.LastError;
                messages.Add($"Channel {channel.Channel}: {detail}");
            }
        }

        return messages
            .Where(message => !string.IsNullOrWhiteSpace(message))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static OperateStatus ResolveStatus(
        AlertRuleResponse rule,
        AlertRuleHealthResponse? health,
        AlertRuleTestResponse? validation,
        bool hasValidationMessages)
    {
        if (validation is { IsValid: false })
        {
            return new OperateStatus("invalid", "Rule has validation errors and cannot be enabled.");
        }

        var channelFailing = (health?.DeliveryChannels ?? []).Any(channel => IsFailureStatus(channel.Status));
        if (channelFailing)
        {
            return new OperateStatus("failing", "One or more delivery channels are failing.");
        }

        if ((health?.DeliveryFailureCount ?? 0) > 0)
        {
            return new OperateStatus("degraded", "Delivery retries are above the warning threshold.");
        }

        if (hasValidationMessages)
        {
            return new OperateStatus("warning", "Rule has open validation notes.");
        }

        return rule.IsActive
            ? new OperateStatus("healthy", "Rule is enabled and delivery channels are healthy.")
            : new OperateStatus("disabled", "Rule is valid but disabled by operator choice.");
    }

    private static string BuildDescription(AlertRuleResponse rule) =>
        $"{Titleize(rule.TriggerType)} trigger, cooldown "
        + $"{rule.CooldownSeconds.ToString(CultureInfo.InvariantCulture)}s, severity {rule.Severity}.";

    private static string ChannelDetail(AlertChannelValidationResponse channel) =>
        string.IsNullOrWhiteSpace(channel.Message) ? channel.Status : channel.Message;

    private static int? SecondsToMinutes(int? seconds)
    {
        if (seconds is not { } value || value <= 0)
        {
            return null;
        }

        // Round up so a 90s dwell surfaces as 2 minutes rather than silently
        // truncating to 1; the inverse multiplies minutes back to seconds.
        return (value + 59) / 60;
    }

    private static bool TryParseZoneId(string? zoneId, out long? parsed)
    {
        parsed = null;
        if (string.IsNullOrWhiteSpace(zoneId))
        {
            return false;
        }

        if (long.TryParse(zoneId.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            parsed = value;
            return true;
        }

        return false;
    }

    private static T? TryParse<T>(string json, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo)
        where T : class
    {
        try
        {
            return JsonSerializer.Deserialize(json, typeInfo);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsFailureStatus(string status) =>
        NormalizeToken(status) is "failing" or "unauthorized" or "rate limited" or "disabled";

    private static string FormatTimestamp(DateTimeOffset? value) =>
        value is { } resolved
            ? resolved.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture)
            : "never";

    private static string NormalizeToken(string? value) =>
        (value ?? string.Empty).Trim().ToLowerInvariant().Replace("_", " ", StringComparison.Ordinal);

    private static string Titleize(string value)
    {
        var normalized = NormalizeToken(value);
        return normalized.Length == 0
            ? normalized
            : char.ToUpperInvariant(normalized[0]) + normalized[1..];
    }
}
