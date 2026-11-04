using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public interface IConsoleAlertRulesClient
{
    Task<OperateSectionResult<AlertRuleResponse>> GetRuleAsync(
        long ruleId,
        CancellationToken cancellationToken = default);

    Task<OperateSectionResult<AlertRuleHealthResponse>> GetRuleHealthAsync(
        long ruleId,
        CancellationToken cancellationToken = default);

    Task<OperateSectionResult<AlertRuleTestResponse>> TestRuleAsync(
        AlertRuleRequest rule,
        CancellationToken cancellationToken = default);

    Task<OperateSectionResult<AlertRuleResponse>> SaveRuleAsync(
        long? ruleId,
        AlertRuleRequest rule,
        CancellationToken cancellationToken = default);

    // Activation changes without a save or a test of the rule body.
    Task<OperateSectionResult<AlertRuleResponse>> SetRuleEnabledAsync(
        long ruleId,
        bool enabled,
        CancellationToken cancellationToken = default);
}
