namespace Honua.Console.Shell.Models;

public sealed record OpsHealthView(
    OperateStatus Overall,
    string GeneratedAt,
    OpsHealthChecksView Health,
    OpsServingLatencyView ServingLatency,
    OpsGpQueueView Geoprocessing,
    OpsAlertDispatchView AlertDispatch,
    OpsDeployReadinessView Deploy,
    OpsDatabaseView Database,
    IReadOnlyList<OpsManagedTargetView>? ManagedTargetObservations = null)
{
    public IReadOnlyList<OpsManagedTargetView> ManagedTargets => ManagedTargetObservations ?? [];
    public int BreachCount =>
        ServingLatency.Rows.Count(row => row.Status.IsBreach)
        + (Geoprocessing.Status.IsBreach ? 1 : 0)
        + (AlertDispatch.Status.IsBreach ? 1 : 0)
        + (Deploy.Status.IsBreach ? 1 : 0)
        + (Database.PoolUtilization.Status.IsBreach ? 1 : 0)
        + (Database.CacheHitRatio.Status.IsBreach ? 1 : 0)
        + (Database.ErrorRate.Status.IsBreach ? 1 : 0);
}

public sealed record OpsHealthChecksView(
    OperateStatus Status,
    string DurationLabel,
    IReadOnlyList<OpsHealthCheckEntryView> Entries);

public sealed record OpsHealthCheckEntryView(
    string Name,
    OperateStatus Status,
    string DurationLabel,
    string Description);

public sealed record OpsServingLatencyView(
    string WindowLabel,
    IReadOnlyList<OpsServingLatencyRowView> Rows)
{
    public bool HasBreach => Rows.Any(row => row.Status.IsFailure || row.Status.NormalizedState == "warning");
}

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

public sealed record OpsGpQueueView(
    int TotalActive,
    bool Available,
    OperateStatus Status,
    IReadOnlyList<OpsGpQueueBucketView> Buckets);

public sealed record OpsGpQueueBucketView(
    string Status,
    string Backend,
    int Count);

public sealed record OpsAlertDispatchView(
    OperateStatus Status,
    bool DispatcherRunning,
    bool DispatcherEnabled,
    bool StoragePollFailing,
    string LastPollLabel,
    string PendingLabel,
    string DeadLetteredLabel,
    bool HasDeadLetters);

public sealed record OpsDeployReadinessView(
    OperateStatus Status,
    bool ReadyForCoordinatedDeploy,
    int PendingMigrationsCount,
    int PendingContractScriptsCount,
    OpsPlatformReleaseView PlatformRelease);

public sealed record OpsPlatformReleaseView(
    string ReleaseLabel,
    bool ReleaseDeclared,
    bool IsCoVersioned,
    OperateStatus CoVersionStatus,
    IReadOnlyList<string> SkewedIds);

public sealed record OpsDatabaseView(
    OperateMetricBar PoolUtilization,
    OperateMetricBar CacheHitRatio,
    OperateMetricBar ErrorRate,
    int ActiveConnections,
    long ConnectionAcquisitionTimeouts,
    long ConnectionAcquisitionFailures);

public sealed record OpsManagedTargetView(string TargetId, bool Available, IReadOnlyList<OpsManagedServiceView> Services);

public sealed record OpsManagedServiceView(string Name, string ObservedStatus, OperateStatus Status);
