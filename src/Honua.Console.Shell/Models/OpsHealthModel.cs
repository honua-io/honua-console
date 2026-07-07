namespace Honua.Console.Shell.Models;

/// <summary>
/// The Ops Health at-a-glance view: the consolidated snapshot mapped into color-coded
/// sections, each carrying an <see cref="OperateStatus"/> so the page renders the shared
/// status-chip vocabulary (healthy/warning/critical/neutral). Produced from the live
/// server snapshot — never fabricated (Console Patterns Charter section 11).
/// </summary>
public sealed record OpsHealthView(
    OperateStatus Overall,
    string GeneratedAt,
    OpsHealthChecksView Health,
    OpsServingLatencyView ServingLatency,
    OpsGpQueueView Geoprocessing,
    OpsAlertDispatchView AlertDispatch,
    OpsDeployReadinessView Deploy,
    OpsDatabaseView Database)
{
    /// <summary>
    /// The total count of breaching sections across the snapshot (console#292 ops-summary
    /// strip's "SLO breaches" element): every per-protocol serving-latency breach, plus the
    /// geoprocessing, alert-dispatch, deploy-readiness, and database vitals sections whenever
    /// their status renders as a warning/danger badge. Mirrors the same <see cref="OperateStatus.IsBreach"/>
    /// test the health page uses to decide whether a badge needs a deep link, so the strip's
    /// count and the page's dead-end-free badges never disagree.
    /// </summary>
    public int BreachCount =>
        ServingLatency.Rows.Count(row => row.Status.IsBreach)
        + (Geoprocessing.Status.IsBreach ? 1 : 0)
        + (AlertDispatch.Status.IsBreach ? 1 : 0)
        + (Deploy.Status.IsBreach ? 1 : 0)
        + (Database.PoolUtilization.Status.IsBreach ? 1 : 0)
        + (Database.CacheHitRatio.Status.IsBreach ? 1 : 0)
        + (Database.ErrorRate.Status.IsBreach ? 1 : 0);
}

/// <summary>Comprehensive health-check roll-up view.</summary>
public sealed record OpsHealthChecksView(
    OperateStatus Status,
    string DurationLabel,
    IReadOnlyList<OpsHealthCheckEntryView> Entries);

/// <summary>A single health-check entry view.</summary>
public sealed record OpsHealthCheckEntryView(
    string Name,
    OperateStatus Status,
    string DurationLabel,
    string Description);

/// <summary>Serving-latency section view (per-protocol percentile table).</summary>
public sealed record OpsServingLatencyView(
    string WindowLabel,
    IReadOnlyList<OpsServingLatencyRowView> Rows)
{
    /// <summary>Gets a value indicating whether any protocol row breaches a latency/error threshold.</summary>
    public bool HasBreach => Rows.Any(row => row.Status.IsFailure || row.Status.NormalizedState == "warning");
}

/// <summary>Per-protocol serving-latency row view. The row status flags SLO breaches.</summary>
public sealed record OpsServingLatencyRowView(
    string Protocol,
    long RequestCount,
    long ErrorCount,
    string ErrorRateLabel,
    string P50Label,
    string P95Label,
    string P99Label,
    string MaxLabel,
    OperateStatus Status);

/// <summary>Geoprocessing queue-depth view.</summary>
public sealed record OpsGpQueueView(
    int TotalActive,
    bool Available,
    OperateStatus Status,
    IReadOnlyList<OpsGpQueueBucketView> Buckets);

/// <summary>A single GP queue-depth bucket view.</summary>
public sealed record OpsGpQueueBucketView(
    string Status,
    string Backend,
    int Count);

/// <summary>Alert-dispatch backlog / dead-letter view.</summary>
public sealed record OpsAlertDispatchView(
    OperateStatus Status,
    bool DispatcherRunning,
    bool DispatcherEnabled,
    bool StoragePollFailing,
    string LastPollLabel,
    string PendingLabel,
    string DeadLetteredLabel,
    bool HasDeadLetters);

/// <summary>Coordinated-deploy readiness + platform-release skew view.</summary>
public sealed record OpsDeployReadinessView(
    OperateStatus Status,
    bool ReadyForCoordinatedDeploy,
    int PendingMigrationsCount,
    int PendingContractScriptsCount,
    OpsPlatformReleaseView PlatformRelease);

/// <summary>Platform-release co-versioning view.</summary>
public sealed record OpsPlatformReleaseView(
    string ReleaseLabel,
    bool ReleaseDeclared,
    bool IsCoVersioned,
    OperateStatus CoVersionStatus,
    IReadOnlyList<string> SkewedIds);

/// <summary>Database connection-pool + cache/error vitals view.</summary>
public sealed record OpsDatabaseView(
    OperateMetricBar PoolUtilization,
    OperateMetricBar CacheHitRatio,
    OperateMetricBar ErrorRate,
    int ActiveConnections,
    long ConnectionAcquisitionTimeouts,
    long ConnectionAcquisitionFailures);
