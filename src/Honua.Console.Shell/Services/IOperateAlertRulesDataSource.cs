using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public interface IOperateAlertRulesDataSource
{
    Task<OperateAlertRulesView> GetRulesAsync(CancellationToken cancellationToken = default);

    Task<OperateAlertRuleDetailView> GetRuleAsync(string ruleId, CancellationToken cancellationToken = default);

    Task<OperateAlertRuleSaveResult> SaveRuleAsync(
        OperateAlertRuleEdit edit,
        CancellationToken cancellationToken = default);

    Task<OperateAlertRuleTestResult> TestRuleAsync(
        OperateAlertRuleDraft draft,
        CancellationToken cancellationToken = default);

    Task<OperateAlertRuleTestResult> TestRuleAsync(
        OperateAlertRuleEdit edit,
        CancellationToken cancellationToken = default);

    Task<OperateAlertRuleSaveResult> CreateRuleAsync(
        OperateAlertRuleDraft draft,
        CancellationToken cancellationToken = default);

    Task<OperateAlertRuleSaveResult> SetRuleEnabledAsync(
        string ruleId,
        bool enabled,
        CancellationToken cancellationToken = default);
}
