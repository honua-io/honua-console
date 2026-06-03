using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Missing-binding implementation of <see cref="IOperateAlertRulesDataSource"/>. This is the only alert
/// rules data source registered in the merged build until the alert rule definition / condition contract
/// (honua-server#1169) lands. Every operation returns no rules plus a single neutral missing-binding state
/// so the list, the detail/editor, and a save render an explicit explanation rather than an empty or
/// fabricated rule surface (Console Patterns Charter section 11). Mirrors
/// <see cref="UnsupportedTemporalCapabilityClient"/>.
/// </summary>
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
}
