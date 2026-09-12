namespace Honua.Console.Shell.Models;

/// <summary>
/// Routes for the alert rule definition surface (UI-042, honua-server#1169). Distinct from the
/// firing-alert evidence route (<c>/operate/alerts/{alertId}</c>).
/// </summary>
public static class OperateAlertRulesRoutes
{
    public const string List = "/operate/alerts/rules";

    public const string Create = "/operate/alerts/rules/new";

    public static string Detail(string ruleId) => $"/operate/alerts/rules/{Uri.EscapeDataString(ruleId)}";
}

/// <summary>
/// Neutral binding/capability state for the alert rules surface. Mirrors the Operate capability-state
/// vocabulary (<c>missing</c>, <c>forbidden</c>, <c>unsupported</c>) so an unbound or denied rules surface
/// renders an explanation rather than an empty editor (Console Patterns Charter section 11).
/// </summary>
public sealed record OperateAlertRulesBindingState(string Surface, string State, string Contract, string Detail)
{
    public const string MissingBinding = "Missing binding";
    public const string Forbidden = "Forbidden";
    public const string Unsupported = "Unsupported";

    public bool IsMissingBinding =>
        string.Equals(State, MissingBinding, StringComparison.OrdinalIgnoreCase);
}

/// <summary>The rules list plus any binding/capability state.</summary>
public sealed record OperateAlertRulesView(
    IReadOnlyList<OperateAlertRule> Rules,
    OperateAlertRulesBindingState? BindingState = null);

/// <summary>One rule's detail and condition definition for the editor.</summary>
public sealed record OperateAlertRuleDetailView(
    OperateAlertRuleDefinition? Rule,
    OperateAlertRulesBindingState? BindingState = null);

/// <summary>
/// A full alert rule definition (list summary fields plus the editable condition + delivery the editor
/// binds). Server-owned shape once honua-server#1169 lands; until then only the Unsupported source is wired.
/// </summary>
public sealed record OperateAlertRuleDefinition(
    string RuleId,
    string Name,
    string RuleType,
    bool Enabled,
    OperateStatus Status,
    string Description,
    OperateAlertRuleCondition Condition,
    string DeliverySummary,
    IReadOnlyList<string> DeliveryChannels,
    string LastEvaluatedAt,
    int ActiveIncidentCount,
    int DeliveryFailureCount,
    IReadOnlyList<string> ValidationMessages);

/// <summary>
/// The condition a rule evaluates. The same shape covers realtime/threshold/geofence/dwell: a metric or
/// geofence subject, a comparison operator, a threshold, a window, and (for geofence/dwell) a zone + dwell
/// minimum. The editor binds these directly.
/// </summary>
public sealed record OperateAlertRuleCondition(
    string Subject,
    string Operator,
    string Threshold,
    string Window,
    string? GeofenceZoneId = null,
    int? DwellMinutes = null);

/// <summary>An edit submitted by the rule editor; the server validates + persists it.</summary>
public sealed record OperateAlertRuleEdit(
    string RuleId,
    string Name,
    bool Enabled,
    OperateAlertRuleCondition Condition,
    IReadOnlyList<string> DeliveryChannels);

/// <summary>Result of a rule save: the persisted rule on success, or a binding state on a blocked write.</summary>
public sealed record OperateAlertRuleSaveResult(
    OperateAlertRuleDefinition? Rule,
    OperateAlertRulesBindingState? BindingState = null)
{
    public bool Succeeded => BindingState is null && Rule is not null;

    public static OperateAlertRuleSaveResult Blocked(OperateAlertRulesBindingState state) =>
        new(Rule: null, state);
}

public sealed record OperateAlertRuleDraft(
    string ServiceId,
    int LayerId,
    string Name,
    string TriggerType,
    OperateAlertRuleCondition Condition,
    IReadOnlyList<string> DeliveryChannels);

public sealed record OperateAlertRuleTestResult(
    bool IsValid,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings,
    OperateAlertRulesBindingState? BindingState = null)
{
    public bool Succeeded => BindingState is null && IsValid;
}
