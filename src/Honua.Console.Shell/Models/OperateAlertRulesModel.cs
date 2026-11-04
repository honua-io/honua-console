namespace Honua.Console.Shell.Models;

public static class OperateAlertRulesRoutes
{
    public const string List = "/operate/alerts/rules";

    public const string Create = "/operate/alerts/rules/new";

    public static string Detail(string ruleId) => $"/operate/alerts/rules/{Uri.EscapeDataString(ruleId)}";
}

public sealed record OperateAlertRulesBindingState(string Surface, string State, string Contract, string Detail)
{
    public const string MissingBinding = "Missing binding";
    public const string Forbidden = "Forbidden";
    public const string Unsupported = "Unsupported";

    public bool IsMissingBinding =>
        string.Equals(State, MissingBinding, StringComparison.OrdinalIgnoreCase);
}

public sealed record OperateAlertRulesView(
    IReadOnlyList<OperateAlertRule> Rules,
    OperateAlertRulesBindingState? BindingState = null);

public sealed record OperateAlertRuleDetailView(
    OperateAlertRuleDefinition? Rule,
    OperateAlertRulesBindingState? BindingState = null);

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

public sealed record OperateAlertRuleCondition(
    string Subject,
    string Operator,
    string Threshold,
    string Window,
    string? GeofenceZoneId = null,
    int? DwellMinutes = null);

public sealed record OperateAlertRuleEdit(
    string RuleId,
    string Name,
    bool Enabled,
    OperateAlertRuleCondition Condition,
    IReadOnlyList<string> DeliveryChannels);

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
