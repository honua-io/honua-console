namespace Honua.Console.Shell.Models;

// #102 — "Import from Esri" wizard run + parity scorecard projections.
//
// The migration RUN engine is driven by honua-devops (per the issue-122 handoff "migration-run API owner"
// open item). Console does not own that contract and there is no Console-consumable run API yet, so the run
// and scorecard render against an explicit missing-binding state via IEsriMigrationRunDataSource — Console
// never fabricates run progress, per-item results, or parity numbers (Console Patterns Charter section 11).
// These records are the shapes the Run/Scorecard surfaces consume the moment honua-devops exposes a
// Console-bindable run contract.

/// <summary>Lifecycle state of a per-item migration step.</summary>
public enum MigrationItemState
{
    Queued,
    Running,
    Done,
    Failed,
    Skipped,
}

/// <summary>Parity outcome of a finished item, used by the scorecard.</summary>
public enum MigrationParityResult
{
    Pass,
    Degraded,
    Binding,
    Failed,
    Skipped,
}

/// <summary>One selected item in the wizard's Map step (mixed content types + per-item fidelity + blockers).</summary>
public sealed record MigrationSelectionItem(
    string SourceName,
    string SourceType,
    string TargetKind,
    ImportFidelity Fidelity,
    string Blockers);

/// <summary>The wizard's Map-step view: the source context, selected items, and the conversion summary.</summary>
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

/// <summary>One row of the Run step's per-item progress table.</summary>
public sealed record MigrationRunItem(
    string Name,
    string Type,
    MigrationItemState State,
    string Result,
    int Percent = 0,
    bool Retryable = false);

/// <summary>The Run step's live view: migration id, driver, overall progress, and per-item rows.</summary>
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

/// <summary>One per-item finding in the parity scorecard, with its findings text and produced output.</summary>
public sealed record MigrationScorecardItem(
    string Name,
    MigrationParityResult Result,
    string Findings,
    string Output);

/// <summary>What landed after a run completed (counts per produced content type).</summary>
public sealed record MigrationLandedCount(string Label, int Count);

/// <summary>
/// The Scorecard step — the migration record. Carries pass/degraded/binding/failed counts, the overall
/// parity percentage + segment widths for the bar, per-item findings, and the "what landed" tallies.
/// </summary>
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
    /// <summary>Overall parity percent (passed share of the total).</summary>
    public int ParityPercent => TotalItems == 0 ? 0 : (int)Math.Round(PassCount * 100.0 / TotalItems);

    public int PassPercent => Segment(PassCount);

    public int DegradedPercent => Segment(DegradedCount);

    public int BindingPercent => Segment(BindingCount);

    public int FailedPercent => Segment(FailedCount);

    private int Segment(int count) => TotalItems == 0 ? 0 : (int)Math.Round(count * 100.0 / TotalItems);
}

/// <summary>
/// A binding/permission/unsupported surface for the migration-run surfaces (#102), mirroring the
/// capability-state pattern used across Catalogs/RBAC/Share. Carries the surface, state, owning contract,
/// and an explanatory detail rendered by the missing-binding view.
/// </summary>
public sealed record MigrationRunCapabilityState(
    string Surface,
    string State,
    string Contract,
    string Detail);

/// <summary>Outcome of loading the wizard plan: a plan view, or capability states when it cannot be read.</summary>
public sealed record MigrationPlanLoad(MigrationPlanView? Plan, IReadOnlyList<MigrationRunCapabilityState> CapabilityStates)
{
    public bool HasPlan => Plan is not null;
}

/// <summary>Outcome of loading run progress: a run view, or capability states when it cannot be read.</summary>
public sealed record MigrationRunLoad(MigrationRunView? Run, IReadOnlyList<MigrationRunCapabilityState> CapabilityStates)
{
    public bool HasRun => Run is not null;
}

/// <summary>Outcome of loading the scorecard: a scorecard view, or capability states when it cannot be read.</summary>
public sealed record MigrationScorecardLoad(MigrationScorecardView? Scorecard, IReadOnlyList<MigrationRunCapabilityState> CapabilityStates)
{
    public bool HasScorecard => Scorecard is not null;
}
