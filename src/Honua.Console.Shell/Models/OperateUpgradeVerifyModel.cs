using Honua.Console.Shell.Services;

namespace Honua.Console.Shell.Models;

/// <summary>
/// Post-upgrade verification summary (console#290 addendum item 1b): a before/after diff of
/// the live ops-health snapshot, captured once when the operator engages the upgrade card and
/// again once the governed upgrade operation reaches <c>Succeeded</c> — "you upgraded; here's
/// the proof it's healthy" instead of upgrading on faith. Both snapshots are real reads (the
/// ops-health data source); when either read was denied, the corresponding row renders
/// "unknown" rather than fabricating a status.
/// </summary>
public sealed record OperateUpgradeVerifyRow(string Label, string Before, string After, OperateStatus Verdict);

public sealed record OperateUpgradeVerifySummaryData(IReadOnlyList<OperateUpgradeVerifyRow> Rows, bool BeforeAvailable, bool AfterAvailable)
{
    public static OperateUpgradeVerifySummaryData Build(
        OperateSectionResult<OpsHealthView>? before,
        OperateSectionResult<OpsHealthView>? after)
    {
        var beforeView = before is { IsAllowed: true } ? before.Value : null;
        var afterView = after is { IsAllowed: true } ? after.Value : null;

        var rows = new List<OperateUpgradeVerifyRow>
        {
            Row("Overall status", beforeView?.Overall, afterView?.Overall),
            Row("Health checks", beforeView?.Health.Status, afterView?.Health.Status),
            Row("Alert dispatch", beforeView?.AlertDispatch.Status, afterView?.AlertDispatch.Status),
            Row("Database", beforeView?.Database.ErrorRate.Status, afterView?.Database.ErrorRate.Status),
        };

        if (beforeView is not null && afterView is not null)
        {
            rows.Add(new OperateUpgradeVerifyRow(
                "SLO breaches",
                beforeView.BreachCount.ToString(),
                afterView.BreachCount.ToString(),
                BreachCountVerdict(beforeView.BreachCount, afterView.BreachCount)));
        }

        return new OperateUpgradeVerifySummaryData(rows, beforeView is not null, afterView is not null);
    }

    private static OperateUpgradeVerifyRow Row(string label, OperateStatus? beforeStatus, OperateStatus? afterStatus) => new(
        label,
        beforeStatus?.Label ?? "unknown",
        afterStatus?.Label ?? "unknown",
        Verdict(beforeStatus, afterStatus));

    private static OperateStatus Verdict(OperateStatus? beforeStatus, OperateStatus? afterStatus)
    {
        if (beforeStatus is null || afterStatus is null)
        {
            return new OperateStatus("unknown", "One of the two snapshots is unavailable.");
        }

        if (afterStatus.IsFailure && !beforeStatus.IsFailure)
        {
            return new OperateStatus("critical", "Regressed to a failure state after the upgrade.");
        }

        if (afterStatus.IsBreach && !beforeStatus.IsBreach)
        {
            return new OperateStatus("warning", "Regressed to a breach state after the upgrade.");
        }

        if (beforeStatus.IsBreach && !afterStatus.IsBreach)
        {
            return new OperateStatus("healthy", "Improved after the upgrade.");
        }

        return new OperateStatus("healthy", "Unchanged or improved after the upgrade.");
    }

    private static OperateStatus BreachCountVerdict(int before, int after) => after switch
    {
        _ when after > before => new OperateStatus("warning", $"Breach count rose from {before} to {after}."),
        _ when after < before => new OperateStatus("healthy", $"Breach count fell from {before} to {after}."),
        _ => new OperateStatus("healthy", "Breach count unchanged."),
    };
}
