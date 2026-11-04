namespace Honua.Console.Shell.Models;

// #102 — "Import from Esri" wizard run + parity scorecard projections.
//
// The migration RUN engine is driven by honua-devops (per the issue-122 handoff "migration-run API owner"
// open item). Console does not own that contract and there is no Console-consumable run API yet, so the run
// and scorecard render against an explicit missing-binding state via IEsriMigrationRunDataSource — Console
// never fabricates run progress, per-item results, or parity numbers (Console Patterns Charter section 11).
// These records are the shapes the Run/Scorecard surfaces consume the moment honua-devops exposes a
// Console-bindable run contract.

public enum MigrationItemState
{
    Queued,
    Running,
    Done,
    Failed,
    Skipped,
}

public enum MigrationParityResult
{
    Pass,
    Degraded,
    Binding,
    Failed,
    Skipped,
}

public sealed record MigrationSelectionItem(
    string SourceName,
    string SourceType,
    string TargetKind,
    ImportFidelity Fidelity,
    string Blockers);

public sealed record MigrationPlanView(
    string SourceLabel,
    string SourceOrg,
    int SelectedItems,
    int ContentTypes,
    string RunDriver,
    IReadOnlyList<MigrationSelectionItem> Items)
{
    public int CleanCount => Items.Count(i => i.Fidelity == ImportFidelity.Clean);

    public int DegradeCount => Items.Count(i => i.Fidelity == ImportFidelity.Degrade);

    public int ManualCount => Items.Count(i => i.Fidelity == ImportFidelity.Manual);

    public int DropCount => Items.Count(i => i.Fidelity == ImportFidelity.Drop);
}

public sealed record MigrationRunItem(
    string Name,
    string Type,
    MigrationItemState State,
    string Result,
    int Percent = 0,
    bool Retryable = false);

public sealed record MigrationRunView(
    string MigrationId,
    string Driver,
    string TargetEnv,
    string StartedBy,
    bool Resumable,
    int CompletedItems,
    int TotalItems,
    int FailedItems,
    string EstimatedRemaining,
    IReadOnlyList<MigrationRunItem> Items)
{
    public int PercentComplete => TotalItems == 0 ? 0 : (int)Math.Round(CompletedItems * 100.0 / TotalItems);
}

public sealed record MigrationScorecardItem(
    string Name,
    MigrationParityResult Result,
    string Findings,
    string Output);

public sealed record MigrationLandedCount(string Label, int Count);

public sealed record MigrationScorecardView(
    string MigrationId,
    int TotalItems,
    string Duration,
    int PassCount,
    int DegradedCount,
    int BindingCount,
    int FailedCount,
    IReadOnlyList<MigrationScorecardItem> Items,
    IReadOnlyList<MigrationLandedCount> Landed,
    IReadOnlyList<string> NextSteps)
{
    public int ParityPercent => TotalItems == 0 ? 0 : (int)Math.Round(PassCount * 100.0 / TotalItems);

    public int PassPercent => Segment(PassCount);

    public int DegradedPercent => Segment(DegradedCount);

    public int BindingPercent => Segment(BindingCount);

    public int FailedPercent => Segment(FailedCount);

    private int Segment(int count) => TotalItems == 0 ? 0 : (int)Math.Round(count * 100.0 / TotalItems);
}

public sealed record MigrationRunCapabilityState(
    string Surface,
    string State,
    string Contract,
    string Detail);

public sealed record MigrationPlanLoad(MigrationPlanView? Plan, IReadOnlyList<MigrationRunCapabilityState> CapabilityStates)
{
    public bool HasPlan => Plan is not null;
}

public sealed record MigrationRunLoad(MigrationRunView? Run, IReadOnlyList<MigrationRunCapabilityState> CapabilityStates)
{
    public bool HasRun => Run is not null;
}

public sealed record MigrationScorecardLoad(MigrationScorecardView? Scorecard, IReadOnlyList<MigrationRunCapabilityState> CapabilityStates)
{
    public bool HasScorecard => Scorecard is not null;
}
