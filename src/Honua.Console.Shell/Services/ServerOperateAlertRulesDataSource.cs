using System.Globalization;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Server-bound implementation of <see cref="IOperateAlertRulesDataSource"/>. The rule LIST reads the live
/// realtime/alert rules the connected honua-server advertises through
/// <see cref="IConsoleOperateObservabilityClient.GetRulesAsync"/> (the shipped
/// <c>/api/v1/admin/observability</c> rules projection). The rule DETAIL/condition editor and the rule SAVE
/// write bind the SHIPPED alert-rule DEFINITION admin contract (honua-server#1169,
/// <c>/api/v{version}/admin/alerts/rules…</c>) through <see cref="IConsoleAlertRulesClient"/>: a rule read
/// hydrates the editor from <c>GET /rules/{ruleId}</c> (+ <c>/health</c>); a save validates the draft via
/// <c>POST /rules/test</c> and persists with <c>PUT /rules/{ruleId}</c>. On any failure (404/forbidden/
/// unreachable/unsupported) or a draft that fails validation, the operation returns the appropriate
/// <see cref="OperateAlertRulesBindingState"/> (or a blocked save) and never fabricates a rule or condition
/// (Console Patterns Charter section 11).
/// </summary>
public sealed class ServerOperateAlertRulesDataSource : IOperateAlertRulesDataSource
{
    private const string Surface = "Alert rules";
    private const string DefinitionContract = "honua-server#1169 / /api/v1/admin/alerts/rules";

    private readonly IConsoleOperateObservabilityClient _observability;
    private readonly IConsoleAlertRulesClient _rules;

    public ServerOperateAlertRulesDataSource(
        IConsoleOperateObservabilityClient observability,
        IConsoleAlertRulesClient rules)
    {
        _observability = observability ?? throw new ArgumentNullException(nameof(observability));
        _rules = rules ?? throw new ArgumentNullException(nameof(rules));
    }

    public async Task<OperateAlertRulesView> GetRulesAsync(CancellationToken cancellationToken = default)
    {
        var result = await _observability.GetRulesAsync(cancellationToken);
        if (!result.IsAllowed || result.Value is null)
        {
            // Map the section status to the rules binding vocabulary so the list renders the explanation.
            return new OperateAlertRulesView([], BindingState(result.Status, result.Message));
        }

        return new OperateAlertRulesView(result.Value.Rules);
    }

    public async Task<OperateAlertRuleDetailView> GetRuleAsync(
        string ruleId,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseRuleId(ruleId, out var id))
        {
            return new OperateAlertRuleDetailView(
                Rule: null,
                new OperateAlertRulesBindingState(
                    Surface,
                    OperateAlertRulesBindingState.MissingBinding,
                    DefinitionContract,
                    $"Alert rule '{ruleId}' is not a valid rule identifier."));
        }

        var ruleResult = await _rules.GetRuleAsync(id, cancellationToken).ConfigureAwait(false);
        if (!ruleResult.IsAllowed || ruleResult.Value is null)
        {
            return new OperateAlertRuleDetailView(Rule: null, BindingState(ruleResult.Status, ruleResult.Message));
        }

        var rule = ruleResult.Value;

        // Health (delivery-state, last-evaluated, incident/failure counts) and the
        // draft validation (errors/warnings/per-channel delivery state) are
        // independent sub-reads; degrade gracefully if either is absent so the
        // editor still renders the persisted rule + condition.
        var healthTask = _rules.GetRuleHealthAsync(id, cancellationToken);
        var validationTask = _rules.TestRuleAsync(ToRequest(rule), cancellationToken);
        await Task.WhenAll(healthTask, validationTask).ConfigureAwait(false);

        var healthResult = await healthTask.ConfigureAwait(false);
        var validationResult = await validationTask.ConfigureAwait(false);

        var definition = OperateAlertRuleMapper.MapDefinition(
            rule,
            healthResult.IsAllowed ? healthResult.Value : null,
            validationResult.IsAllowed ? validationResult.Value : null);

        return new OperateAlertRuleDetailView(definition);
    }

    public async Task<OperateAlertRuleSaveResult> SaveRuleAsync(
        OperateAlertRuleEdit edit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edit);

        if (!TryParseRuleId(edit.RuleId, out var id))
        {
            return OperateAlertRuleSaveResult.Blocked(new OperateAlertRulesBindingState(
                Surface,
                OperateAlertRulesBindingState.MissingBinding,
                DefinitionContract,
                $"Alert rule '{edit.RuleId}' is not a valid rule identifier."));
        }

        // The editor authors only name/enablement/condition/channels; the immutable
        // fields (service/layer/cooldown/severity/edition) come from the persisted
        // rule, so a save reads the current rule first and overlays the edit.
        var currentResult = await _rules.GetRuleAsync(id, cancellationToken).ConfigureAwait(false);
        if (!currentResult.IsAllowed || currentResult.Value is null)
        {
            return OperateAlertRuleSaveResult.Blocked(BindingState(currentResult.Status, currentResult.Message));
        }

        if (!OperateAlertRuleMapper.TryBuildRequest(edit, currentResult.Value, out var request, out var buildError))
        {
            // The edited condition cannot be represented on the wire faithfully;
            // report the honest validation reason rather than guessing a payload.
            return OperateAlertRuleSaveResult.Blocked(new OperateAlertRulesBindingState(
                Surface,
                OperateAlertRulesBindingState.MissingBinding,
                DefinitionContract,
                buildError));
        }

        // Pre-validate the draft via /rules/test: a draft that fails validation must
        // not be persisted, and the editor surfaces the full error/warning list.
        var validationResult = await _rules.TestRuleAsync(request, cancellationToken).ConfigureAwait(false);
        if (validationResult.IsAllowed && validationResult.Value is { IsValid: false } invalid)
        {
            return OperateAlertRuleSaveResult.Blocked(new OperateAlertRulesBindingState(
                Surface,
                OperateAlertRulesBindingState.MissingBinding,
                DefinitionContract,
                BuildValidationDetail(invalid)));
        }

        var saveResult = await _rules.SaveRuleAsync(id, request, cancellationToken).ConfigureAwait(false);
        if (!saveResult.IsAllowed || saveResult.Value is null)
        {
            return OperateAlertRuleSaveResult.Blocked(BindingState(saveResult.Status, saveResult.Message));
        }

        // Hydrate the persisted rule's editor view (health is best-effort).
        var healthResult = await _rules.GetRuleHealthAsync(id, cancellationToken).ConfigureAwait(false);
        var definition = OperateAlertRuleMapper.MapDefinition(
            saveResult.Value,
            healthResult.IsAllowed ? healthResult.Value : null,
            validationResult.IsAllowed ? validationResult.Value : null);

        return new OperateAlertRuleSaveResult(definition);
    }

    public async Task<OperateAlertRuleTestResult> TestRuleAsync(
        OperateAlertRuleDraft draft,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (!TryBuildDraftRequest(draft, isActive: false, out var request, out var error))
        {
            return new OperateAlertRuleTestResult(false, [error], []);
        }

        var result = await _rules.TestRuleAsync(request, cancellationToken).ConfigureAwait(false);
        if (!result.IsAllowed || result.Value is null)
        {
            return new OperateAlertRuleTestResult(false, [], [], BindingState(result.Status, result.Message));
        }

        return new OperateAlertRuleTestResult(
            result.Value.IsValid,
            result.Value.Errors ?? [],
            result.Value.Warnings ?? []);
    }

    public async Task<OperateAlertRuleTestResult> TestRuleAsync(
        OperateAlertRuleEdit edit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edit);
        if (!TryParseRuleId(edit.RuleId, out var id))
        {
            return new OperateAlertRuleTestResult(false, ["Rule id is invalid."], []);
        }

        var current = await _rules.GetRuleAsync(id, cancellationToken).ConfigureAwait(false);
        if (!current.IsAllowed || current.Value is null)
        {
            return new OperateAlertRuleTestResult(false, [], [], BindingState(current.Status, current.Message));
        }

        if (!OperateAlertRuleMapper.TryBuildRequest(edit, current.Value, out var request, out var error))
        {
            return new OperateAlertRuleTestResult(false, [error], []);
        }

        request = request with { IsActive = false };
        var result = await _rules.TestRuleAsync(request, cancellationToken).ConfigureAwait(false);
        if (!result.IsAllowed || result.Value is null)
        {
            return new OperateAlertRuleTestResult(false, [], [], BindingState(result.Status, result.Message));
        }

        return new OperateAlertRuleTestResult(result.Value.IsValid, result.Value.Errors ?? [], result.Value.Warnings ?? []);
    }

    public async Task<OperateAlertRuleSaveResult> CreateRuleAsync(
        OperateAlertRuleDraft draft,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (!TryBuildDraftRequest(draft, isActive: false, out var request, out var error))
        {
            return OperateAlertRuleSaveResult.Blocked(new OperateAlertRulesBindingState(
                Surface, OperateAlertRulesBindingState.MissingBinding, DefinitionContract, error));
        }

        var result = await _rules.SaveRuleAsync(ruleId: null, request, cancellationToken).ConfigureAwait(false);
        if (!result.IsAllowed || result.Value is null)
        {
            return OperateAlertRuleSaveResult.Blocked(BindingState(result.Status, result.Message));
        }

        var definition = OperateAlertRuleMapper.MapDefinition(result.Value, health: null, validation: null);
        return new OperateAlertRuleSaveResult(definition);
    }

    public async Task<OperateAlertRuleSaveResult> SetRuleEnabledAsync(
        string ruleId,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseRuleId(ruleId, out var id))
        {
            return OperateAlertRuleSaveResult.Blocked(new OperateAlertRulesBindingState(
                Surface, OperateAlertRulesBindingState.MissingBinding, DefinitionContract,
                $"Alert rule '{ruleId}' is not a valid rule identifier."));
        }

        var result = await _rules.SetRuleEnabledAsync(id, enabled, cancellationToken).ConfigureAwait(false);
        if (!result.IsAllowed || result.Value is null)
        {
            return OperateAlertRuleSaveResult.Blocked(BindingState(result.Status, result.Message));
        }

        var health = await _rules.GetRuleHealthAsync(id, cancellationToken).ConfigureAwait(false);
        return new OperateAlertRuleSaveResult(OperateAlertRuleMapper.MapDefinition(
            result.Value, health.IsAllowed ? health.Value : null, validation: null));
    }

    private static bool TryBuildDraftRequest(
        OperateAlertRuleDraft draft,
        bool isActive,
        out AlertRuleRequest request,
        out string error)
    {
        error = string.Empty;
        string conditions;
        if (string.Equals(draft.TriggerType, "threshold", StringComparison.OrdinalIgnoreCase))
        {
            if (!double.TryParse(draft.Condition.Threshold, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                request = new AlertRuleRequest();
                error = "Threshold rules require a numeric threshold.";
                return false;
            }

            conditions = System.Text.Json.JsonSerializer.Serialize(
                new AlertThresholdConditions { Field = draft.Condition.Subject, Operator = draft.Condition.Operator, Value = value },
                AlertAdminJsonContext.Default.AlertThresholdConditions);
        }
        else if (string.Equals(draft.TriggerType, "dwell", StringComparison.OrdinalIgnoreCase))
        {
            conditions = System.Text.Json.JsonSerializer.Serialize(
                new AlertDwellConditions { DwellSeconds = draft.Condition.DwellMinutes * 60 },
                AlertAdminJsonContext.Default.AlertDwellConditions);
        }
        else
        {
            conditions = "{}";
        }

        long? zoneId = null;
        if (!string.IsNullOrWhiteSpace(draft.Condition.GeofenceZoneId)
            && !long.TryParse(draft.Condition.GeofenceZoneId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedZone))
        {
            request = new AlertRuleRequest();
            error = "Geofence zone must be a numeric server zone id.";
            return false;
        }
        else if (!string.IsNullOrWhiteSpace(draft.Condition.GeofenceZoneId))
        {
            zoneId = parsedZone;
        }

        request = new AlertRuleRequest
        {
            ServiceId = draft.ServiceId.Trim(),
            LayerId = draft.LayerId,
            ZoneId = zoneId,
            RuleName = draft.Name.Trim(),
            TriggerType = draft.TriggerType.Trim(),
            ConditionsJson = conditions,
            CooldownSeconds = 0,
            Severity = "warning",
            EditionRequired = "pro",
            Channels = [.. draft.DeliveryChannels],
            IsActive = isActive
        };
        return true;
    }

    private static AlertRuleRequest ToRequest(AlertRuleResponse rule) => new()
    {
        ServiceId = rule.ServiceId,
        LayerId = rule.LayerId,
        ZoneId = rule.ZoneId,
        RuleName = rule.RuleName,
        TriggerType = rule.TriggerType,
        ConditionsJson = string.IsNullOrWhiteSpace(rule.ConditionsJson) ? "{}" : rule.ConditionsJson,
        CooldownSeconds = rule.CooldownSeconds,
        Severity = string.IsNullOrWhiteSpace(rule.Severity) ? "warning" : rule.Severity,
        EditionRequired = string.IsNullOrWhiteSpace(rule.EditionRequired) ? "pro" : rule.EditionRequired,
        Channels = (rule.Channels ?? []).ToArray(),
        IsActive = rule.IsActive
    };

    private static string BuildValidationDetail(AlertRuleTestResponse validation)
    {
        var errors = (validation.Errors ?? []).Where(message => !string.IsNullOrWhiteSpace(message)).ToArray();
        if (errors.Length > 0)
        {
            return "The alert rule could not be saved: " + string.Join(" ", errors);
        }

        return "The alert rule draft failed server validation and was not saved.";
    }

    private static OperateAlertRulesBindingState BindingState(OperateSectionStatus status, string message) =>
        new(
            Surface,
            MapStatus(status),
            DefinitionContract,
            string.IsNullOrWhiteSpace(message)
                ? OperateSectionPresentation.FallbackMessage(status)
                : message);

    private static bool TryParseRuleId(string? ruleId, out long id) =>
        long.TryParse((ruleId ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out id);

    private static string MapStatus(OperateSectionStatus status) => status switch
    {
        OperateSectionStatus.Forbidden => OperateAlertRulesBindingState.Forbidden,
        OperateSectionStatus.Unsupported => OperateAlertRulesBindingState.Unsupported,
        _ => OperateAlertRulesBindingState.MissingBinding
    };
}
