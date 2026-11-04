using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public sealed class UnsupportedOperateAlertRulesDataSource : IOperateAlertRulesDataSource
{
    internal const string Surface = "Alert rules";
    internal const string Contract = "honua-server#1169";

    internal const string Detail =
        "Alert rule definitions (realtime / geofence / threshold / dwell) bind to the server-owned alert "
        + "rule API (honua-server#1169). Configure Honua:Server:BaseUrl (or HONUA_SERVER_BASE_URL) and wait "
        + "for that contract to land; Console will not fabricate alert rules from a mock.";

    private static readonly OperateAlertRulesBindingState MissingBinding =
        new(Surface, OperateAlertRulesBindingState.MissingBinding, Contract, Detail);

    public Task<OperateAlertRulesView> GetRulesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new OperateAlertRulesView([], MissingBinding));

    public Task<OperateAlertRuleDetailView> GetRuleAsync(
        string ruleId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new OperateAlertRuleDetailView(Rule: null, MissingBinding));

    public Task<OperateAlertRuleSaveResult> SaveRuleAsync(
        OperateAlertRuleEdit edit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edit);
        return Task.FromResult(OperateAlertRuleSaveResult.Blocked(MissingBinding));
    }

    public Task<OperateAlertRuleTestResult> TestRuleAsync(
        OperateAlertRuleDraft draft,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new OperateAlertRuleTestResult(false, [], [], MissingBinding));

    public Task<OperateAlertRuleTestResult> TestRuleAsync(
        OperateAlertRuleEdit edit,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new OperateAlertRuleTestResult(false, [], [], MissingBinding));

    public Task<OperateAlertRuleSaveResult> CreateRuleAsync(
        OperateAlertRuleDraft draft,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(OperateAlertRuleSaveResult.Blocked(MissingBinding));

    public Task<OperateAlertRuleSaveResult> SetRuleEnabledAsync(
        string ruleId,
        bool enabled,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(OperateAlertRuleSaveResult.Blocked(MissingBinding));
}
