using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Server-bound implementation of <see cref="IOperateAlertRulesDataSource"/>. The rule LIST reads the live
/// realtime/alert rules the connected honua-server already advertises through
/// <see cref="IConsoleOperateObservabilityClient.GetRulesAsync"/> (the shipped
/// <c>/api/v1/admin/observability</c> rules projection), so the list surface binds real data when an
/// environment is connected. The rule DETAIL/condition editor and the rule SAVE write require the alert rule
/// DEFINITION contract (honua-server#1169), which has not shipped; until it does, those operations return an
/// explicit missing-binding state rather than fabricating an editable condition (Console Patterns Charter
/// section 11). When honua-server#1169 lands, wire its read/write here behind the same observability HTTP
/// boundary and drop the missing-binding fallbacks.
/// </summary>
public sealed class ServerOperateAlertRulesDataSource : IOperateAlertRulesDataSource
{
    private const string Surface = "Alert rules";

    // The list binds live; the editor + save await the alert rule definition/condition contract.
    private const string DefinitionContract = "honua-server#1169";

    private static readonly OperateAlertRulesBindingState DefinitionMissing = new(
        Surface,
        OperateAlertRulesBindingState.MissingBinding,
        DefinitionContract,
        "The connected honua-server lists alert rules but does not yet expose the rule definition / condition "
        + "editor contract (honua-server#1169). The rule list is live; the condition editor and rule save "
        + "activate once that contract ships. Console will not fabricate an editable condition.");

    private readonly IConsoleOperateObservabilityClient _observability;

    public ServerOperateAlertRulesDataSource(IConsoleOperateObservabilityClient observability)
    {
        _observability = observability ?? throw new ArgumentNullException(nameof(observability));
    }

    public async Task<OperateAlertRulesView> GetRulesAsync(CancellationToken cancellationToken = default)
    {
        var result = await _observability.GetRulesAsync(cancellationToken);
        if (!result.IsAllowed || result.Value is null)
        {
            // Map the section status to the rules binding vocabulary so the list renders the explanation.
            return new OperateAlertRulesView([], new OperateAlertRulesBindingState(
                Surface,
                MapStatus(result.Status),
                "honua-server#1169 / /api/v1/admin/observability",
                string.IsNullOrWhiteSpace(result.Message)
                    ? OperateSectionPresentation.FallbackMessage(result.Status)
                    : result.Message));
        }

        return new OperateAlertRulesView(result.Value.Rules);
    }

    public Task<OperateAlertRuleDetailView> GetRuleAsync(
        string ruleId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new OperateAlertRuleDetailView(Rule: null, DefinitionMissing));

    public Task<OperateAlertRuleSaveResult> SaveRuleAsync(
        OperateAlertRuleEdit edit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edit);
        return Task.FromResult(OperateAlertRuleSaveResult.Blocked(DefinitionMissing));
    }

    private static string MapStatus(OperateSectionStatus status) => status switch
    {
        OperateSectionStatus.Forbidden => OperateAlertRulesBindingState.Forbidden,
        OperateSectionStatus.Unsupported => OperateAlertRulesBindingState.Unsupported,
        _ => OperateAlertRulesBindingState.MissingBinding
    };
}
