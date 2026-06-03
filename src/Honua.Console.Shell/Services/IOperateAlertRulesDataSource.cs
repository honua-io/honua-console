using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Reads and (later) writes the realtime / geofence / threshold / dwell alert RULE definitions that back
/// the alert rules list (<c>/operate/alerts/rules</c>) and the rule detail / condition editor
/// (<c>/operate/alerts/rules/{ruleId}</c>) — UI-042, honua-server#1169.
///
/// This is the rule-DEFINITION surface (author/enable/disable a rule and edit its condition + delivery),
/// distinct from the firing-alert evidence surface (<c>/operate/alerts/{alertId}</c>) that already binds the
/// observability client. Per Console Patterns Charter section 11, the merged build registers the
/// <see cref="UnsupportedOperateAlertRulesDataSource"/> (an honest missing-binding state) until the alert
/// rule definition/condition contract (honua-server#1169) lands; the <c>Server*</c> implementation activates
/// only when a server base URL is configured. Every result carries an <see cref="OperateAlertRulesBindingState"/>
/// so a list/detail/save renders the binding/forbidden/unsupported explanation instead of fabricated rules.
/// </summary>
public interface IOperateAlertRulesDataSource
{
    /// <summary>Lists the alert rule definitions plus any binding/capability state.</summary>
    Task<OperateAlertRulesView> GetRulesAsync(CancellationToken cancellationToken = default);

    /// <summary>Loads one rule's detail + condition definition for the editor, or a binding state.</summary>
    Task<OperateAlertRuleDetailView> GetRuleAsync(string ruleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves an edited rule condition/delivery/enablement. The server records the change and returns the
    /// persisted rule; a blocked write carries a binding state instead of claiming success.
    /// </summary>
    Task<OperateAlertRuleSaveResult> SaveRuleAsync(
        OperateAlertRuleEdit edit,
        CancellationToken cancellationToken = default);
}
