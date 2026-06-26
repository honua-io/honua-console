using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Validation;

/// <summary>
/// Stable console-owned field keys for the Collect automation editor. The client validator
/// (<see cref="CollectAutomationValidator"/>) and the inline render surfaces share these so a client
/// finding lands on the same input the editor renders. Automation-level keys are constants; per-rule and
/// per-action keys are derived from list position.
/// </summary>
public static class CollectAutomationFieldKeys
{
    public const string Name = "automation.name";
    public const string FormId = "automation.formId";
    public const string MaxCascadeDepth = "automation.maxCascadeDepth";
    public const string Rules = "automation.rules";

    /// <summary>Trigger-field key for rule <paramref name="ruleIndex"/>.</summary>
    public static string RuleTriggerField(int ruleIndex) => $"automation.rule[{ruleIndex}].triggerField";

    /// <summary>Action-target key for rule <paramref name="ruleIndex"/> action <paramref name="actionIndex"/>.</summary>
    public static string ActionTarget(int ruleIndex, int actionIndex) =>
        $"automation.rule[{ruleIndex}].action[{actionIndex}].target";
}

/// <summary>
/// Pure client-side validator for the Collect automation editor, mirroring the Studio validators: it
/// examines the console-owned <see cref="CollectAutomationDraft"/> and emits field-addressable
/// <see cref="ConsoleFieldError"/> findings so the editor can surface them inline. It complements — never
/// replaces — the engine's own validation. It covers the rules expressible against console-owned state:
/// <list type="bullet">
///   <item>a non-empty automation name and bound form id;</item>
///   <item>cascade-depth bounds (1-32) feeding the engine's deterministic loop guard;</item>
///   <item>at least one rule, and at least one action per rule;</item>
///   <item>each rule declares a supported trigger; a field-change trigger names a field;</item>
///   <item>each action declares a supported kind and the target the kind requires.</item>
/// </list>
/// </summary>
public sealed class CollectAutomationValidator : IFieldValidator<CollectAutomationDraft>
{
    /// <summary>Shared singleton; the validator holds no state.</summary>
    public static CollectAutomationValidator Instance { get; } = new();

    /// <inheritdoc />
    public IReadOnlyList<ConsoleFieldError> Evaluate(CollectAutomationDraft state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var errors = new List<ConsoleFieldError>();

        if (string.IsNullOrWhiteSpace(state.Name))
        {
            errors.Add(Blocker(CollectAutomationFieldKeys.Name, "automation.name.required", "Give the automation a name."));
        }

        if (string.IsNullOrWhiteSpace(state.FormId))
        {
            errors.Add(Blocker(
                CollectAutomationFieldKeys.FormId,
                "automation.formId.required",
                "Bind the automation to a form so the engine can wire its Data Events."));
        }

        if (!NumericBoundsRule.IsWithin(state.MaxCascadeDepth, 1, 32))
        {
            errors.Add(Error(
                CollectAutomationFieldKeys.MaxCascadeDepth,
                "automation.maxCascadeDepth.range",
                "Cascade depth (the engine loop guard) must be between 1 and 32."));
        }

        if (state.Rules.Count == 0)
        {
            errors.Add(Blocker(CollectAutomationFieldKeys.Rules, "automation.rules.empty", "Add at least one rule."));
        }

        for (var ruleIndex = 0; ruleIndex < state.Rules.Count; ruleIndex++)
        {
            EvaluateRule(state.Rules[ruleIndex], ruleIndex, errors);
        }

        return errors;
    }

    private static void EvaluateRule(CollectAutomationRule rule, int ruleIndex, List<ConsoleFieldError> errors)
    {
        if (!CollectAutomationContractValues.TriggerKinds.Contains(rule.Trigger))
        {
            errors.Add(Error(
                CollectAutomationFieldKeys.Rules,
                "automation.rule.trigger.unsupported",
                $"Rule '{RuleLabel(rule, ruleIndex)}' declares an unsupported trigger '{rule.Trigger}'."));
        }

        // A field-change trigger must name the field whose change fires it.
        if (string.Equals(rule.Trigger, CollectAutomationContractValues.TriggerFieldChange, StringComparison.Ordinal)
            && string.IsNullOrWhiteSpace(rule.TriggerField))
        {
            errors.Add(Error(
                CollectAutomationFieldKeys.RuleTriggerField(ruleIndex),
                "automation.rule.triggerField.required",
                $"Rule '{RuleLabel(rule, ruleIndex)}' fires on a field change but names no field."));
        }

        if (rule.Actions.Count == 0)
        {
            errors.Add(Error(
                CollectAutomationFieldKeys.Rules,
                "automation.rule.actions.empty",
                $"Rule '{RuleLabel(rule, ruleIndex)}' has no actions. Add at least one action or remove the rule."));
        }

        for (var actionIndex = 0; actionIndex < rule.Actions.Count; actionIndex++)
        {
            EvaluateAction(rule.Actions[actionIndex], ruleIndex, actionIndex, errors);
        }
    }

    private static void EvaluateAction(
        CollectAutomationAction action,
        int ruleIndex,
        int actionIndex,
        List<ConsoleFieldError> errors)
    {
        if (!CollectAutomationContractValues.ActionKinds.Contains(action.Kind))
        {
            errors.Add(Error(
                CollectAutomationFieldKeys.ActionTarget(ruleIndex, actionIndex),
                "automation.action.kind.unsupported",
                $"Action {actionIndex + 1} declares an unsupported kind '{action.Kind}'."));
            return;
        }

        // set/compute/tag/notify/http/open-url all write somewhere; require a target. validate/ai are
        // expression-only and need no target.
        if (RequiresTarget(action.Kind) && string.IsNullOrWhiteSpace(action.Target))
        {
            errors.Add(Error(
                CollectAutomationFieldKeys.ActionTarget(ruleIndex, actionIndex),
                "automation.action.target.required",
                $"A '{action.Kind}' action needs a target ({DescribeTarget(action.Kind)})."));
        }
    }

    private static bool RequiresTarget(string kind) =>
        kind is CollectAutomationContractValues.ActionSet
            or CollectAutomationContractValues.ActionCompute
            or CollectAutomationContractValues.ActionTag
            or CollectAutomationContractValues.ActionNotify
            or CollectAutomationContractValues.ActionHttp
            or CollectAutomationContractValues.ActionOpenUrl;

    private static string DescribeTarget(string kind) => kind switch
    {
        CollectAutomationContractValues.ActionSet or CollectAutomationContractValues.ActionCompute => "the field to write",
        CollectAutomationContractValues.ActionTag => "the tag name",
        CollectAutomationContractValues.ActionNotify => "the notification channel",
        CollectAutomationContractValues.ActionHttp => "the request URL or outbox key",
        CollectAutomationContractValues.ActionOpenUrl => "the URL to open",
        _ => "a target",
    };

    private static string RuleLabel(CollectAutomationRule rule, int ruleIndex) =>
        string.IsNullOrWhiteSpace(rule.Name) ? $"rule {ruleIndex + 1}" : rule.Name;

    private static ConsoleFieldError Blocker(string key, string code, string message) =>
        new(key, code, ConsoleValidationSeverity.Blocker, message);

    private static ConsoleFieldError Error(string key, string code, string message) =>
        new(key, code, ConsoleValidationSeverity.Error, message);
}
