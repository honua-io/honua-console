using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Reads and writes the alert-rule DEFINITION authoring contract shipped by
/// honua-server#1169 (<c>/api/v{version}/admin/alerts/rules…</c>): get a rule, its
/// operational health, validate a draft, and create/update a rule. Each call
/// returns an <see cref="OperateSectionResult{T}"/> carrying a status that the
/// rule editor maps to the binding/forbidden/unsupported surface, so a denied or
/// unreachable server is reported honestly rather than fabricated (Console
/// Patterns Charter section 11).
/// </summary>
public interface IConsoleAlertRulesClient
{
    /// <summary>GET /rules/{ruleId} — the persisted rule definition.</summary>
    Task<OperateSectionResult<AlertRuleResponse>> GetRuleAsync(
        long ruleId,
        CancellationToken cancellationToken = default);

    /// <summary>GET /rules/{ruleId}/health — per-rule delivery-state + operational health.</summary>
    Task<OperateSectionResult<AlertRuleHealthResponse>> GetRuleHealthAsync(
        long ruleId,
        CancellationToken cancellationToken = default);

    /// <summary>POST /rules/test — validate a draft rule (errors, warnings, per-channel delivery state).</summary>
    Task<OperateSectionResult<AlertRuleTestResponse>> TestRuleAsync(
        AlertRuleRequest rule,
        CancellationToken cancellationToken = default);

    /// <summary>POST /rules (create) or PUT /rules/{ruleId} (update) — persist a rule.</summary>
    Task<OperateSectionResult<AlertRuleResponse>> SaveRuleAsync(
        long? ruleId,
        AlertRuleRequest rule,
        CancellationToken cancellationToken = default);

    /// <summary>PUT /rules/{ruleId}/enabled — change activation independently from save/test.</summary>
    Task<OperateSectionResult<AlertRuleResponse>> SetRuleEnabledAsync(
        long ruleId,
        bool enabled,
        CancellationToken cancellationToken = default);
}
