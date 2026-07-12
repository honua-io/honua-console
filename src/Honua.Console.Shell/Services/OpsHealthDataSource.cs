using System.Globalization;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Loads the consolidated ops-health snapshot and maps it into the color-coded Ops Health
/// view. Degrades honestly to the shared section-status surface (missing/forbidden/
/// unsupported/unavailable) when the environment is unbound or the server denies the read —
/// never fabricated data (Console Patterns Charter section 11).
/// </summary>
public interface IOpsHealthDataSource
{
    /// <summary>Reads and maps the ops-health snapshot for the active environment.</summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The mapped view, or a non-allowed section status.</returns>
    Task<OperateSectionResult<OpsHealthView>> GetSnapshotAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads and maps the ops-health trend history for the active environment (console#288): the
    /// selectable-window/resolution/per-replica trend charts on the Ops Health page. Degrades to
    /// <see cref="OperateSectionStatus.Unsupported"/> against an older server that does not yet
    /// expose the history route.
    /// </summary>
    /// <param name="selection">The window/resolution/per-replica selection.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The mapped trend view, or a non-allowed section status.</returns>
    Task<OperateSectionResult<OpsHealthTrendView>> GetHistoryAsync(
        OpsHealthTrendRangeSelection selection,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// One selectable trend range on the Ops Health page (console#288): a human label, the
/// server-facing window/resolution pair, and whether to request the per-replica breakdown. The
/// three offered ranges pair each rollup tier with its natural default window (mirroring the
/// server's own <c>OpsHealthHistoryQuery.DefaultWindow</c>), so every combination is one the
/// server always has real rollup rows for once the store has been running long enough.
/// </summary>
public sealed record OpsHealthTrendRangeSelection(string Label, string Window, string Resolution, bool PerReplica)
{
    public static readonly OpsHealthTrendRangeSelection LastHour = new("Last hour", "1h", "1m", false);
    public static readonly OpsHealthTrendRangeSelection Last24Hours = new("Last 24 hours", "24h", "5m", false);
    public static readonly OpsHealthTrendRangeSelection Last7Days = new("Last 7 days", "7d", "1h", false);

    public static IReadOnlyList<OpsHealthTrendRangeSelection> Options { get; } = [LastHour, Last24Hours, Last7Days];

    public OpsHealthTrendRangeSelection WithPerReplica(bool perReplica) => this with { PerReplica = perReplica };
}

/// <summary>Default <see cref="IOpsHealthDataSource"/> over <see cref="IConsoleOpsHealthClient"/>.</summary>
public sealed class OpsHealthDataSource : IOpsHealthDataSource
{
    // Serving-latency SLO thresholds used to flag a protocol row. Display-only heuristics
    // (the server does not label rows); kept conservative to surface genuine breaches.
    private const double P95BreachMs = 1000.0;
    private const double ErrorRateWarn = 0.01;
    private const double ErrorRateCritical = 0.05;

    private readonly IConsoleOpsHealthClient _client;

    /// <summary>Initializes a new instance of the <see cref="OpsHealthDataSource"/> class.</summary>
    /// <param name="client">The ops-health client.</param>
    public OpsHealthDataSource(IConsoleOpsHealthClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    /// <inheritdoc />
    public async Task<OperateSectionResult<OpsHealthView>> GetSnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        var result = await _client.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        if (!result.IsAllowed)
        {
            return OperateSectionResult<OpsHealthView>.Denied(result.Status, result.Message);
        }

        return OperateSectionResult<OpsHealthView>.Allowed(Map(result.Value!));
    }

    /// <inheritdoc />
    public async Task<OperateSectionResult<OpsHealthTrendView>> GetHistoryAsync(
        OpsHealthTrendRangeSelection selection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selection);

        var result = await _client
            .GetHistoryAsync(new ConsoleOpsHealthHistoryQuery(selection.Window, selection.Resolution, selection.PerReplica), cancellationToken)
            .ConfigureAwait(false);
        if (!result.IsAllowed)
        {
            return OperateSectionResult<OpsHealthTrendView>.Denied(result.Status, result.Message);
        }

        return OperateSectionResult<OpsHealthTrendView>.Allowed(OpsHealthTrendMapper.Map(result.Value!, selection.Label));
    }

    internal static OpsHealthView Map(OpsHealthSnapshotResponse snapshot)
    {
        return new OpsHealthView(
            ReportStatus(snapshot.OverallStatus),
            FormatTimestamp(snapshot.GeneratedAt),
            MapHealth(snapshot.Health),
            MapServingLatency(snapshot.ServingLatency),
            MapGeoprocessing(snapshot.Geoprocessing),
            MapAlertDispatch(snapshot.AlertDispatch),
            MapDeploy(snapshot.Deploy),
            MapDatabase(snapshot.Database));
    }

    private static OpsHealthChecksView MapHealth(OpsHealthChecksResponse? health)
    {
        if (health is null)
        {
            return new OpsHealthChecksView(Neutral("Health checks were not reported."), "unknown", []);
        }

        var entries = (health.Entries ?? [])
            .Select(entry => new OpsHealthCheckEntryView(
                string.IsNullOrWhiteSpace(entry.Name) ? "(unnamed)" : entry.Name!,
                ReportStatus(entry.Status),
                FormatMs(entry.DurationMs),
                string.IsNullOrWhiteSpace(entry.Description) ? string.Empty : entry.Description!))
            .ToArray();

        return new OpsHealthChecksView(ReportStatus(health.Status), FormatMs(health.TotalDurationMs), entries);
    }

    private static OpsServingLatencyView MapServingLatency(OpsServingLatencyResponse? latency)
    {
        if (latency is null)
        {
            return new OpsServingLatencyView("unknown", []);
        }

        var rows = (latency.Protocols ?? [])
            .Select(p => new OpsServingLatencyRowView(
                string.IsNullOrWhiteSpace(p.Protocol) ? "(unknown)" : p.Protocol!,
                p.RequestCount,
                p.ErrorCount,
                Percent(p.ErrorRate),
                FormatMs(p.P50Ms),
                FormatMs(p.P95Ms),
                FormatMs(p.P99Ms),
                FormatMs(p.MaxMs),
                LatencyRowStatus(p.P95Ms, p.ErrorRate)))
            .ToArray();

        var windowLabel = latency.WindowSeconds > 0
            ? $"last {latency.WindowSeconds.ToString("0", CultureInfo.InvariantCulture)}s"
            : "rolling window";

        return new OpsServingLatencyView(windowLabel, rows);
    }

    private static OperateStatus LatencyRowStatus(double p95Ms, double errorRate)
    {
        if (errorRate >= ErrorRateCritical)
        {
            return Danger($"Error rate {Percent(errorRate)} exceeds the critical threshold.");
        }

        if (errorRate >= ErrorRateWarn || p95Ms >= P95BreachMs)
        {
            return Warning($"p95 {FormatMs(p95Ms)} / error rate {Percent(errorRate)} breaches an SLO threshold.");
        }

        return Success($"p95 {FormatMs(p95Ms)}, error rate {Percent(errorRate)}.");
    }

    private static OpsGpQueueView MapGeoprocessing(OpsGpQueueResponse? gp)
    {
        if (gp is null || !gp.Available)
        {
            return new OpsGpQueueView(
                gp?.TotalActive ?? 0,
                false,
                Neutral("Geoprocessing queue telemetry isn't available on the connected server yet."),
                []);
        }

        var buckets = (gp.Buckets ?? [])
            .Select(b => new OpsGpQueueBucketView(
                string.IsNullOrWhiteSpace(b.Status) ? "(unknown)" : b.Status!,
                string.IsNullOrWhiteSpace(b.Backend) ? "(unknown)" : b.Backend!,
                b.Count))
            .ToArray();

        var status = gp.TotalActive == 0
            ? Success("No active geoprocessing jobs.")
            : Info($"{gp.TotalActive} active geoprocessing job(s).");

        return new OpsGpQueueView(gp.TotalActive, true, status, buckets);
    }

    private static OpsAlertDispatchView MapAlertDispatch(OpsAlertDispatchResponse? dispatch)
    {
        if (dispatch is null)
        {
            return new OpsAlertDispatchView(
                Neutral("Alert-dispatch telemetry was not reported."),
                false, false, false, "unknown", "unknown", "unknown", false);
        }

        var deadLettered = dispatch.DeadLetteredCount ?? 0;
        var pending = dispatch.PendingCount ?? 0;
        var hasDeadLetters = deadLettered > 0;

        OperateStatus status;
        if (!dispatch.DispatcherEnabled)
        {
            status = Neutral("The alert pipeline is disabled by configuration.");
        }
        else if (dispatch.StoragePollFailing || !dispatch.DispatcherRunning)
        {
            status = Danger("The alert dispatcher is not polling the backlog store.");
        }
        else if (hasDeadLetters)
        {
            status = Warning($"{deadLettered} alert(s) dead-lettered.");
        }
        else if (pending > 0)
        {
            status = Info($"{pending} alert(s) pending dispatch.");
        }
        else
        {
            status = Success("Alert dispatch is healthy with no backlog.");
        }

        return new OpsAlertDispatchView(
            status,
            dispatch.DispatcherRunning,
            dispatch.DispatcherEnabled,
            dispatch.StoragePollFailing,
            dispatch.LastPollAt is { } at ? FormatTimestamp(at) : "never",
            dispatch.PendingCount is { } p ? p.ToString(CultureInfo.InvariantCulture) : "unavailable",
            dispatch.DeadLetteredCount is { } d ? d.ToString(CultureInfo.InvariantCulture) : "unavailable",
            hasDeadLetters);
    }

    private static OpsDeployReadinessView MapDeploy(OpsDeployReadinessResponse? deploy)
    {
        if (deploy is null)
        {
            return new OpsDeployReadinessView(
                Neutral("Deploy readiness was not reported."),
                false, 0, 0,
                new OpsPlatformReleaseView("not declared", false, false, Neutral("No platform release is declared."), []));
        }

        var status = deploy.ReadyForCoordinatedDeploy
            ? Success("Ready for a coordinated deploy.")
            : Warning("A coordinated deploy is blocked by pending contract scripts.");

        var release = deploy.PlatformRelease;
        var skewed = release?.SkewedIds ?? [];
        OperateStatus coVersionStatus;
        if (release is null || !release.ReleaseDeclared)
        {
            coVersionStatus = Neutral("No platform release is declared.");
        }
        else if (release.IsCoVersioned)
        {
            coVersionStatus = Success("All planes are co-versioned with the declared release.");
        }
        else
        {
            coVersionStatus = Warning($"{skewed.Count} plane(s) skewed from the declared release.");
        }

        var platformRelease = new OpsPlatformReleaseView(
            string.IsNullOrWhiteSpace(release?.ReleaseVersion) ? "not declared" : release!.ReleaseVersion!,
            release?.ReleaseDeclared ?? false,
            release?.IsCoVersioned ?? false,
            coVersionStatus,
            skewed);

        return new OpsDeployReadinessView(
            status,
            deploy.ReadyForCoordinatedDeploy,
            deploy.PendingMigrationsCount,
            deploy.PendingContractScriptsCount,
            platformRelease);
    }

    private static OpsDatabaseView MapDatabase(OpsDatabaseResponse? database)
    {
        if (database is null)
        {
            return new OpsDatabaseView(
                NoDataBar(),
                NoDataBar(),
                NoDataBar(),
                0, 0, 0);
        }

        var poolBar = database.HasConnectionPoolData && database.ConnectionPoolUtilization is { } util
            ? Bar(util * 100.0, Percent(util), SaturationStatus(util), hasData: true)
            : NoDataBar();

        // Cache hit ratio is "higher is better": green when healthy, danger when starved.
        var cacheStatus = database.CacheHitRatio >= 0.8
            ? Success($"Cache hit ratio is healthy ({Percent(database.CacheHitRatio)}).")
            : database.CacheHitRatio >= 0.5
                ? Warning($"Cache hit ratio is below the healthy threshold ({Percent(database.CacheHitRatio)}).")
                : Danger($"Cache hit ratio is critically low ({Percent(database.CacheHitRatio)}).");
        var cacheBar = Bar(database.CacheHitRatio * 100.0, Percent(database.CacheHitRatio), cacheStatus, hasData: true);

        var errorStatus = database.ErrorRate >= ErrorRateCritical
            ? Danger($"Server error rate is very high ({Percent(database.ErrorRate)}).")
            : database.ErrorRate >= ErrorRateWarn
                ? Warning($"Server error rate is elevated ({Percent(database.ErrorRate)}).")
                : Success($"Server error rate is healthy ({Percent(database.ErrorRate)}).");
        var errorBar = Bar(database.ErrorRate * 100.0, Percent(database.ErrorRate), errorStatus, hasData: true);

        return new OpsDatabaseView(
            poolBar,
            cacheBar,
            errorBar,
            database.ActiveConnections,
            database.ConnectionAcquisitionTimeouts,
            database.ConnectionAcquisitionFailures);
    }

    // --- shared helpers ---------------------------------------------------------

    private static OperateStatus ReportStatus(string? status) =>
        (status?.Trim().ToLowerInvariant()) switch
        {
            "healthy" => Success("Healthy."),
            "degraded" => Warning("Degraded."),
            "unhealthy" => Danger("Unhealthy."),
            "ready" => Success("Ready."),
            "blocked" => Warning("Blocked."),
            null or "" => Neutral("Status not reported."),
            _ => Neutral(status!)
        };

    private static OperateStatus SaturationStatus(double ratio) => ratio switch
    {
        >= 0.9 => Danger($"Saturation is very high ({Percent(ratio)})."),
        >= 0.8 => Warning($"Saturation is high ({Percent(ratio)})."),
        _ => Success($"Saturation is healthy ({Percent(ratio)}).")
    };

    private static OperateMetricBar NoDataBar() =>
        new(0, "unavailable", Neutral("Telemetry is unavailable for the active provider."), HasData: false);

    private static OperateMetricBar Bar(double percent, string displayValue, OperateStatus status, bool hasData) =>
        new(percent, displayValue, status, hasData);

    private static OperateStatus Success(string description) => new("healthy", description);

    private static OperateStatus Info(string description) => new("info", description);

    private static OperateStatus Warning(string description) => new("warning", description);

    private static OperateStatus Danger(string description) => new("critical", description);

    private static OperateStatus Neutral(string description) => new("unknown", description);

    private static string Percent(double ratio) =>
        (ratio * 100.0).ToString("0.0", CultureInfo.InvariantCulture) + "%";

    private static string FormatMs(double milliseconds) =>
        milliseconds.ToString("0.0", CultureInfo.InvariantCulture) + " ms";

    private static string FormatTimestamp(DateTimeOffset timestamp) =>
        timestamp == default
            ? "unknown"
            : timestamp.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);
}
