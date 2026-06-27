using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Binds the Operate observability surface to a real honua-server through the
/// <c>/api/v1/admin/...</c> contracts. Base address and bearer token come from
/// the active <see cref="ConsoleEnvironmentProfile"/> and its account session,
/// reusing the connection pattern from
/// <c>NativeHonuaConnectionFactory</c> (BaseAddress = profile.ServerBaseUri,
/// Authorization = Bearer &lt;token&gt;). A single shared <see cref="HttpClient"/>
/// is reused across requests; each request builds an absolute URI and attaches
/// its own auth header, so one client safely serves every environment/section.
/// Deserialization is source-generated for trim/AOT safety.
/// </summary>
public sealed class HttpConsoleOperateObservabilityClient : IConsoleOperateObservabilityClient
{
    private const int DefaultPageSize = 50;

    // Bound the cursor-following job read so a very large board cannot fan out an
    // unbounded number of admin reads. Past this cap the result is flagged partial.
    private const int MaxJobPages = 5;

    // Per-rule health and per-investigation detail are per-item admin reads. Fetch them concurrently to
    // avoid a request waterfall, but cap in-flight concurrency so a large rule/investigation list cannot
    // open dozens-to-hundreds of admin sockets at once on every page open (honua-console#236).
    private const int MaxDetailFanoutConcurrency = 8;

    private readonly HttpClient _http;
    private readonly IConsoleEnvironmentProfileStore _profileStore;
    private readonly IConsoleAccountSessionStore _sessionStore;
    private readonly string? _adminApiKey;

    public HttpConsoleOperateObservabilityClient(
        HttpClient http,
        IConsoleEnvironmentProfileStore profileStore,
        IConsoleAccountSessionStore sessionStore,
        string? adminApiKey = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _profileStore = profileStore ?? throw new ArgumentNullException(nameof(profileStore));
        _sessionStore = sessionStore ?? throw new ArgumentNullException(nameof(sessionStore));
        _adminApiKey = adminApiKey;
    }

    public async Task<OperateSectionResult<OperateFleetOverview>> GetOverviewAsync(
        CancellationToken cancellationToken = default)
    {
        var versionTask = FetchAsync(
            OperateAdminRoutes.Version,
            OperateObservabilityJsonContext.Default.AdminVersionEnvelope,
            cancellationToken);
        var capabilitiesTask = FetchAsync(
            OperateAdminRoutes.Capabilities,
            OperateObservabilityJsonContext.Default.AdminCapabilitiesEnvelope,
            cancellationToken);
        var telemetryTask = FetchAsync(
            OperateAdminRoutes.Telemetry,
            OperateObservabilityJsonContext.Default.OperateTelemetryStatusResponse,
            cancellationToken);
        var migrationsTask = FetchAsync(
            OperateAdminRoutes.Migrations,
            OperateObservabilityJsonContext.Default.OperateMigrationStatusResponse,
            cancellationToken);
        var errorsTask = FetchAsync(
            $"{OperateAdminRoutes.RecentErrors}?limit=5",
            OperateObservabilityJsonContext.Default.OperateRecentErrorsResponse,
            cancellationToken);

        await Task.WhenAll(versionTask, capabilitiesTask, telemetryTask, migrationsTask, errorsTask)
            .ConfigureAwait(false);

        var versionFetch = await versionTask.ConfigureAwait(false);
        var capabilitiesFetch = await capabilitiesTask.ConfigureAwait(false);
        var telemetryFetch = await telemetryTask.ConfigureAwait(false);
        var migrationsFetch = await migrationsTask.ConfigureAwait(false);
        var errorsFetch = await errorsTask.ConfigureAwait(false);
        var profile = versionFetch.Profile
            ?? capabilitiesFetch.Profile
            ?? telemetryFetch.Profile
            ?? migrationsFetch.Profile
            ?? errorsFetch.Profile;
        if (profile is null)
        {
            return OperateSectionResult<OperateFleetOverview>.Denied(versionFetch.Status, versionFetch.Message);
        }

        var version = EnvelopeData(versionFetch);
        var capabilities = EnvelopeData(capabilitiesFetch);
        var telemetry = telemetryFetch.Ok ? telemetryFetch.Value : null;
        var migrations = migrationsFetch.Ok ? migrationsFetch.Value : null;
        var recentErrors = errorsFetch.Ok ? errorsFetch.Value : null;
        var (serverVersion, buildSha) = ResolveServerVersion(version, capabilities);

        var environment = new OperateEnvironmentOverview(
            EnvironmentId: profile.Id,
            Name: string.IsNullOrWhiteSpace(profile.DisplayName) ? profile.Id : profile.DisplayName,
            ServerName: profile.ServerBaseUri.Host,
            Version: serverVersion,
            BuildSha: buildSha,
            Health: ResolveOverviewHealth(
                versionFetch,
                capabilitiesFetch,
                telemetryFetch,
                migrationsFetch,
                errorsFetch,
                version,
                capabilities,
                telemetry,
                migrations,
                recentErrors),
            LastSeen: ResolveOverviewLastSeen(profile, version, telemetry, migrations, recentErrors),
            OwnerTeam: string.IsNullOrWhiteSpace(profile.TenantId) ? profile.EnvironmentKind : profile.TenantId,
            DriftSummary: ResolveOverviewDriftSummary(migrationsFetch, migrations, capabilitiesFetch, capabilities));

        var overview = new OperateFleetOverview(
            Environments: [environment],
            TelemetryFacts: BuildOverviewTelemetryFacts(
                profile,
                versionFetch,
                telemetryFetch,
                migrationsFetch,
                errorsFetch,
                version,
                telemetry,
                migrations,
                recentErrors,
                serverVersion),
            CompatibilityRows: BuildOverviewCompatibilityRows(
                versionFetch,
                capabilitiesFetch,
                telemetryFetch,
                migrationsFetch,
                version,
                capabilities,
                telemetry,
                migrations,
                serverVersion));

        return OperateSectionResult<OperateFleetOverview>.Allowed(overview);
    }

    public async Task<OperateSectionResult<IReadOnlyList<OperateEventRow>>> QueryEventsAsync(
        OperateEventQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var parameters = new OperateEventQueryParameters
        {
            Kind = query.EventType,
            CorrelationId = query.CorrelationId,
            MinSeverity = query.MinSeverity,
            TraceId = query.TraceId,
            RequestId = query.RequestId,
            ServiceId = query.ServiceId,
            ResourceRef = query.ResourceRef,
            Actor = query.Actor,
            OperationId = query.OperationId,
            ReleaseId = query.ReleaseId,
            ChangeSetId = query.ChangeSetId,
            From = query.From,
            To = query.To,
            PageSize = DefaultPageSize
        };

        var fetch = await FetchAsync(
            OperateAdminRoutes.Events + parameters.ToQueryString(),
            OperateObservabilityJsonContext.Default.OperateEventPageResponse,
            cancellationToken).ConfigureAwait(false);

        if (!fetch.Ok)
        {
            return OperateSectionResult<IReadOnlyList<OperateEventRow>>.Denied(fetch.Status, fetch.Message);
        }

        var events = OperateObservabilityMapper.MapEvents(fetch.Value!, fetch.Profile!);
        if (!string.IsNullOrWhiteSpace(query.EnvironmentId))
        {
            events = events
                .Where(item => string.Equals(item.EnvironmentId, query.EnvironmentId.Trim(), StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        return OperateSectionResult<IReadOnlyList<OperateEventRow>>.Allowed(
            events,
            partialResult: fetch.Value!.PartialResult,
            message: fetch.Value!.PartialResult ? "One or more event sources returned a partial result." : string.Empty);
    }

    public async Task<OperateSectionResult<OperateLogsView>> GetLogsAsync(
        CancellationToken cancellationToken = default)
    {
        var fetch = await FetchAsync(
            OperateAdminRoutes.Logs,
            OperateObservabilityJsonContext.Default.OperateLogPageResponse,
            cancellationToken).ConfigureAwait(false);

        return fetch.Ok
            ? OperateSectionResult<OperateLogsView>.Allowed(OperateObservabilityMapper.MapLogs(fetch.Value!))
            : OperateSectionResult<OperateLogsView>.Denied(fetch.Status, fetch.Message);
    }

    public async Task<OperateSectionResult<IReadOnlyList<OperateAlertRecord>>> GetAlertsAsync(
        CancellationToken cancellationToken = default)
    {
        var fetch = await FetchAsync(
            $"{OperateAdminRoutes.Alerts}?pageSize={DefaultPageSize}",
            OperateObservabilityJsonContext.Default.ObservabilityAlertEventPageResponse,
            cancellationToken).ConfigureAwait(false);

        return fetch.Ok
            ? OperateSectionResult<IReadOnlyList<OperateAlertRecord>>.Allowed(OperateObservabilityMapper.MapAlerts(fetch.Value!))
            : OperateSectionResult<IReadOnlyList<OperateAlertRecord>>.Denied(fetch.Status, fetch.Message);
    }

    public async Task<OperateSectionResult<OperateRulesView>> GetRulesAsync(
        CancellationToken cancellationToken = default)
    {
        var rulesFetch = await FetchAsync(
            OperateAdminRoutes.AlertRules,
            OperateObservabilityJsonContext.Default.AlertRuleListEnvelope,
            cancellationToken).ConfigureAwait(false);

        if (!rulesFetch.Ok)
        {
            return OperateSectionResult<OperateRulesView>.Denied(rulesFetch.Status, rulesFetch.Message);
        }

        var rawRules = rulesFetch.Value!.Data ?? [];

        // Rule health (delivery failures, recent triggers, validation signals)
        // is a per-rule endpoint; fetch them in parallel rather than serially to
        // avoid a request waterfall across the rule list.
        var healthByRule = await ResolveRuleHealthAsync(rawRules, cancellationToken).ConfigureAwait(false);
        var rules = rawRules
            .Select(rule => MapRuleWithHealth(rule, healthByRule.GetValueOrDefault(rule.RuleId)))
            .ToArray();

        var zonesFetch = await FetchAsync(
            OperateAdminRoutes.AlertZones,
            OperateObservabilityJsonContext.Default.AlertZoneListEnvelope,
            cancellationToken).ConfigureAwait(false);

        var zones = zonesFetch.Ok
            ? (zonesFetch.Value!.Data ?? []).Select(OperateObservabilityMapper.MapZone).ToArray()
            : [];
        var zonesMessage = zonesFetch.Ok
            ? string.Empty
            : FirstNonBlank(zonesFetch.Message, "Geofence zone metadata is unavailable from the server.");

        return OperateSectionResult<OperateRulesView>.Allowed(new OperateRulesView(
            rules,
            zones,
            zonesFetch.Status,
            zonesMessage));
    }

    public Task<OperateSectionResult<IReadOnlyList<OperateJobRun>>> GetJobsAsync(
        CancellationToken cancellationToken = default) =>
        GetJobsAsync(kind: null, cancellationToken);

    public Task<OperateSectionResult<IReadOnlyList<OperateJobRun>>> GetGeoprocessingJobsAsync(
        CancellationToken cancellationToken = default) =>
        GetJobsAsync(OperateJobKinds.Geoprocessing, cancellationToken);

    public async Task<OperateSectionResult<IReadOnlyList<OperateJobRun>>> GetJobsAsync(
        string? kind,
        CancellationToken cancellationToken = default)
    {
        // The server's jobs endpoint applies the kind filter (parsing the wire
        // enum name), so a kind-scoped surface stays a single server read rather
        // than loading the whole fleet page and filtering client-side. The list
        // is cursor-paginated; follow NextCursor up to a bounded number of pages
        // so boards with more than one page aren't silently truncated at 50, and
        // signal (PartialResult + message) when more pages remain past the cap.
        var jobs = new List<OperateJobRun>();
        string? cursor = null;
        ConsoleEnvironmentProfile? profile = null;
        var truncated = false;

        for (var page = 0; page < MaxJobPages; page++)
        {
            var path = $"{OperateAdminRoutes.Jobs}?limit={DefaultPageSize}";
            if (!string.IsNullOrWhiteSpace(kind))
            {
                path += $"&kind={Uri.EscapeDataString(kind.Trim())}";
            }

            if (!string.IsNullOrWhiteSpace(cursor))
            {
                path += $"&cursor={Uri.EscapeDataString(cursor)}";
            }

            var fetch = await FetchAsync(
                path,
                OperateObservabilityJsonContext.Default.ConsoleJobListResponse,
                cancellationToken).ConfigureAwait(false);

            if (!fetch.Ok)
            {
                // A first-page failure denies the section; a later-page failure
                // keeps the pages already read and flags the result as partial.
                if (jobs.Count == 0)
                {
                    return OperateSectionResult<IReadOnlyList<OperateJobRun>>.Denied(fetch.Status, fetch.Message);
                }

                truncated = true;
                break;
            }

            profile ??= fetch.Profile;
            foreach (var summary in fetch.Value!.Items ?? [])
            {
                jobs.Add(OperateObservabilityMapper.MapJobSummary(summary, fetch.Profile!));
            }

            cursor = fetch.Value!.NextCursor;
            if (string.IsNullOrWhiteSpace(cursor))
            {
                break;
            }

            if (page == MaxJobPages - 1)
            {
                // More pages exist past the page cap; surface that rather than
                // silently cutting the board off.
                truncated = true;
            }
        }

        return OperateSectionResult<IReadOnlyList<OperateJobRun>>.Allowed(
            jobs,
            partialResult: truncated,
            message: truncated
                ? $"Showing the most recent {jobs.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)} jobs; more exist on the server."
                : string.Empty);
    }

    public Task<OperateSectionResult<OperateJobControlOutcome>> CancelJobAsync(
        string jobRunId,
        CancellationToken cancellationToken = default) =>
        ControlJobAsync(jobRunId, OperateAdminRoutes.JobCancel, "cancel", cancellationToken);

    public Task<OperateSectionResult<OperateJobControlOutcome>> RetryJobAsync(
        string jobRunId,
        CancellationToken cancellationToken = default) =>
        ControlJobAsync(jobRunId, OperateAdminRoutes.JobRetry, "retry", cancellationToken);

    private async Task<OperateSectionResult<OperateJobControlOutcome>> ControlJobAsync(
        string jobRunId,
        Func<string, string> routeFactory,
        string action,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobRunId);

        var fetch = await PostAsync(
            routeFactory(jobRunId.Trim()),
            OperateObservabilityJsonContext.Default.ConsoleJobControlResponse,
            action,
            cancellationToken).ConfigureAwait(false);

        if (!fetch.Ok)
        {
            return OperateSectionResult<OperateJobControlOutcome>.Denied(fetch.Status, fetch.Message);
        }

        var response = fetch.Value!;
        return OperateSectionResult<OperateJobControlOutcome>.Allowed(new OperateJobControlOutcome(
            JobRunId: string.IsNullOrWhiteSpace(response.JobId) ? jobRunId.Trim() : response.JobId,
            Action: string.IsNullOrWhiteSpace(response.Action) ? action : response.Action,
            Status: response.Status,
            Message: response.Message,
            CorrelationId: response.CorrelationId ?? string.Empty));
    }

    public async Task<OperateSectionResult<OperateJobRun>> GetJobDetailAsync(
        string jobRunId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobRunId);

        var detailFetch = await FetchAsync(
            OperateAdminRoutes.JobDetail(jobRunId),
            OperateObservabilityJsonContext.Default.ConsoleJobDetail,
            cancellationToken).ConfigureAwait(false);

        if (!detailFetch.Ok)
        {
            return OperateSectionResult<OperateJobRun>.Denied(detailFetch.Status, detailFetch.Message);
        }

        // Logs and artifacts are sub-resources; load them alongside the detail
        // so the job panel renders stages, logs, artifacts, and actions together.
        var logsTask = FetchAsync(
            OperateAdminRoutes.JobLogs(jobRunId),
            OperateObservabilityJsonContext.Default.ConsoleJobLogPageResponse,
            cancellationToken);
        var artifactsTask = FetchAsync(
            OperateAdminRoutes.JobArtifacts(jobRunId),
            OperateObservabilityJsonContext.Default.ConsoleJobArtifactPageResponse,
            cancellationToken);
        await Task.WhenAll(logsTask, artifactsTask).ConfigureAwait(false);

        var logsFetch = await logsTask.ConfigureAwait(false);
        var artifactsFetch = await artifactsTask.ConfigureAwait(false);

        var logs = logsFetch.Ok
            ? OperateObservabilityMapper.MapJobLogs(logsFetch.Value!)
            : [];
        var artifacts = artifactsFetch.Ok
            ? OperateObservabilityMapper.MapJobArtifacts(artifactsFetch.Value!)
            : [];

        return OperateSectionResult<OperateJobRun>.Allowed(
            OperateObservabilityMapper.MapJobDetail(
                detailFetch.Value!,
                logs,
                artifacts,
                detailFetch.Profile!,
                logsFetch.Status,
                logsFetch.Message,
                artifactsFetch.Status,
                artifactsFetch.Message));
    }

    public async Task<OperateSectionResult<OperateJobStepsView>> GetJobStepsAsync(
        string jobRunId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobRunId);

        var fetch = await FetchAsync(
            OperateAdminRoutes.JobSteps(jobRunId),
            OperateObservabilityJsonContext.Default.ConsoleJobStepsResponse,
            cancellationToken).ConfigureAwait(false);

        return fetch.Ok
            ? OperateSectionResult<OperateJobStepsView>.Allowed(OperateObservabilityMapper.MapJobSteps(fetch.Value!))
            : OperateSectionResult<OperateJobStepsView>.Denied(fetch.Status, fetch.Message);
    }

    public async Task<OperateSectionResult<IReadOnlyList<OperateInvestigation>>> GetInvestigationsAsync(
        CancellationToken cancellationToken = default)
    {
        var fetch = await FetchAsync(
            $"{OperateAdminRoutes.Investigations}?pageSize={DefaultPageSize}",
            OperateObservabilityJsonContext.Default.InvestigationPageResponse,
            cancellationToken).ConfigureAwait(false);

        return fetch.Ok
            ? OperateSectionResult<IReadOnlyList<OperateInvestigation>>.Allowed(
                await ResolveInvestigationDetailsAsync(fetch.Value!, cancellationToken).ConfigureAwait(false))
            : OperateSectionResult<IReadOnlyList<OperateInvestigation>>.Denied(fetch.Status, fetch.Message);
    }

    public async Task<OperateSectionResult<IReadOnlyList<OperateRecentError>>> GetRecentErrorsAsync(
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        var safeLimit = limit < 1 ? 1 : limit;
        var fetch = await FetchAsync(
            $"{OperateAdminRoutes.RecentErrors}?limit={safeLimit.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
            OperateObservabilityJsonContext.Default.OperateRecentErrorsResponse,
            cancellationToken).ConfigureAwait(false);

        if (!fetch.Ok)
        {
            return OperateSectionResult<IReadOnlyList<OperateRecentError>>.Denied(fetch.Status, fetch.Message);
        }

        var errors = (fetch.Value!.Errors ?? [])
            .Select(error => new OperateRecentError(
                error.Timestamp,
                error.CorrelationId,
                error.Path,
                error.StatusCode,
                error.Message))
            .ToArray();
        return OperateSectionResult<IReadOnlyList<OperateRecentError>>.Allowed(errors);
    }

    private static T? EnvelopeData<T>(FetchResult<ConsoleApiEnvelope<T>> fetch)
        where T : class =>
        fetch.Ok && fetch.Value!.Success ? fetch.Value.Data : null;

    private static IReadOnlyList<OperateTelemetryFact> BuildOverviewTelemetryFacts(
        ConsoleEnvironmentProfile profile,
        FetchResult<ConsoleApiEnvelope<AdminVersionResponse>> versionFetch,
        FetchResult<OperateTelemetryStatusResponse> telemetryFetch,
        FetchResult<OperateMigrationStatusResponse> migrationsFetch,
        FetchResult<OperateRecentErrorsResponse> errorsFetch,
        AdminVersionResponse? version,
        OperateTelemetryStatusResponse? telemetry,
        OperateMigrationStatusResponse? migrations,
        OperateRecentErrorsResponse? recentErrors,
        string serverVersion)
    {
        var facts = new List<OperateTelemetryFact>
        {
            new(
                profile.Id,
                profile.ServerBaseUri.Host,
                "Admin version",
                EnvelopeStatus(versionFetch, $"Server version {serverVersion} reported by the admin version endpoint."),
                FormatFreshness(NonDefault(version?.ServerTime)),
                OperateObservabilityRoutes.Observability + "#telemetry")
        };

        if (telemetry is null)
        {
            facts.Add(new(
                profile.Id,
                profile.ServerBaseUri.Host,
                "Telemetry status endpoint",
                FetchStatus(telemetryFetch.Status, telemetryFetch.Message, "Telemetry status returned by honua-server."),
                "unknown",
                OperateObservabilityRoutes.Observability + "#telemetry"));
        }
        else
        {
            facts.Add(new(
                profile.Id,
                profile.ServerBaseUri.Host,
                "Trace export",
                ExportStatus("Trace", telemetry.TracingEnabled, telemetry.TraceExportState, telemetry.LastExportError),
                FormatFreshness(NonDefault(telemetry.GeneratedAt)),
                OperateObservabilityRoutes.Observability + "#telemetry"));
            facts.Add(new(
                profile.Id,
                profile.ServerBaseUri.Host,
                "Metrics export",
                ExportStatus("Metrics", telemetry.MetricsEnabled, telemetry.MetricsExportState, telemetry.LastExportError),
                FormatFreshness(NonDefault(telemetry.GeneratedAt)),
                OperateObservabilityRoutes.Observability + "#telemetry"));
            facts.Add(new(
                profile.Id,
                profile.ServerBaseUri.Host,
                "Log export",
                ExportStatus("Log", telemetry.LogsEnabled, telemetry.LogExportState, telemetry.LastExportError),
                FormatFreshness(NonDefault(telemetry.GeneratedAt)),
                OperateObservabilityRoutes.Observability + "#telemetry"));
            facts.Add(new(
                profile.Id,
                profile.ServerBaseUri.Host,
                "Admin realtime",
                RealtimeStatus(telemetry.Realtime ?? new()),
                FormatFreshness(NonDefault(telemetry.GeneratedAt)),
                OperateObservabilityRoutes.Observability + "#telemetry"));
        }

        facts.Add(migrations is null
            ? new OperateTelemetryFact(
                profile.Id,
                profile.ServerBaseUri.Host,
                "Database migrations",
                FetchStatus(migrationsFetch.Status, migrationsFetch.Message, "Migration status returned by honua-server."),
                "unknown",
                OperateObservabilityRoutes.Observability + "#telemetry")
            : new OperateTelemetryFact(
                profile.Id,
                profile.ServerBaseUri.Host,
                "Database migrations",
                MigrationStatus(migrations),
                FormatFreshness(NonDefault(migrations.GeneratedAt)),
                OperateObservabilityRoutes.Observability + "#telemetry"));

        facts.Add(recentErrors is null
            ? new OperateTelemetryFact(
                profile.Id,
                profile.ServerBaseUri.Host,
                "Recent error buffer",
                FetchStatus(errorsFetch.Status, errorsFetch.Message, "Recent error buffer returned by honua-server."),
                "unknown",
                OperateObservabilityRoutes.Observability + "#logs")
            : new OperateTelemetryFact(
                profile.Id,
                profile.ServerBaseUri.Host,
                "Recent error buffer",
                RecentErrorsStatus(recentErrors),
                $"{(recentErrors.Errors ?? []).Count.ToString(System.Globalization.CultureInfo.InvariantCulture)} of {recentErrors.Capacity.ToString(System.Globalization.CultureInfo.InvariantCulture)} retained",
                OperateObservabilityRoutes.Observability + "#logs"));

        return facts;
    }

    private static IReadOnlyList<OperateCompatibilityRow> BuildOverviewCompatibilityRows(
        FetchResult<ConsoleApiEnvelope<AdminVersionResponse>> versionFetch,
        FetchResult<ConsoleApiEnvelope<AdminCapabilitiesResponse>> capabilitiesFetch,
        FetchResult<OperateTelemetryStatusResponse> telemetryFetch,
        FetchResult<OperateMigrationStatusResponse> migrationsFetch,
        AdminVersionResponse? version,
        AdminCapabilitiesResponse? capabilities,
        OperateTelemetryStatusResponse? telemetry,
        OperateMigrationStatusResponse? migrations,
        string serverVersion)
    {
        var metadataCurrent = capabilities is null
            ? "unavailable"
            : $"metadata {DisplayValue(capabilities.MetadataApiVersion)} / schema {DisplayValue(capabilities.MetadataSchemaVersion)}";
        var telemetryCurrent = telemetry is null
            ? "unavailable"
            : $"trace {NormalizeServerState(telemetry.TraceExportState)}, metrics {NormalizeServerState(telemetry.MetricsExportState)}, logs {NormalizeServerState(telemetry.LogExportState)}";
        var migrationCurrent = migrations is null
            ? "unavailable"
            : $"{NormalizeServerState(migrations.Status)}; pending {(migrations.PendingScripts ?? []).Count.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

        return
        [
            new OperateCompatibilityRow(
                "Server version contract",
                serverVersion,
                "GET /api/v1/admin/version",
                EnvelopeStatus(versionFetch, $"Server time {FormatFreshness(NonDefault(version?.ServerTime))}."),
                OperateAdminRoutes.Version),
            new OperateCompatibilityRow(
                "Metadata compatibility",
                metadataCurrent,
                "GET /api/v1/admin/capabilities",
                EnvelopeStatus(capabilitiesFetch, "Server advertised admin metadata compatibility."),
                OperateAdminRoutes.Capabilities),
            new OperateCompatibilityRow(
                "Telemetry export contract",
                telemetryCurrent,
                "GET /api/v1/admin/observability/telemetry",
                telemetry is null
                    ? FetchStatus(telemetryFetch.Status, telemetryFetch.Message, "Server advertised telemetry export state.")
                    : TelemetryContractStatus(telemetry),
                OperateAdminRoutes.Telemetry),
            new OperateCompatibilityRow(
                "Migration readiness",
                migrationCurrent,
                "ready without failed migrations",
                migrations is null
                    ? FetchStatus(migrationsFetch.Status, migrationsFetch.Message, "Server advertised migration readiness.")
                    : MigrationStatus(migrations),
                OperateAdminRoutes.Migrations),
            new OperateCompatibilityRow(
                "SDK contract projection",
                "thin HttpClient shim",
                "honua-sdk-dotnet Operate projection",
                new OperateStatus("unknown", "Tracked as the SHIM swap target honua-sdk-dotnet#231."),
                "Honua.Console.Contracts/OperateObservabilityContracts.cs")
        ];
    }

    private static OperateStatus ResolveOverviewHealth(
        FetchResult<ConsoleApiEnvelope<AdminVersionResponse>> versionFetch,
        FetchResult<ConsoleApiEnvelope<AdminCapabilitiesResponse>> capabilitiesFetch,
        FetchResult<OperateTelemetryStatusResponse> telemetryFetch,
        FetchResult<OperateMigrationStatusResponse> migrationsFetch,
        FetchResult<OperateRecentErrorsResponse> errorsFetch,
        AdminVersionResponse? version,
        AdminCapabilitiesResponse? capabilities,
        OperateTelemetryStatusResponse? telemetry,
        OperateMigrationStatusResponse? migrations,
        OperateRecentErrorsResponse? recentErrors)
    {
        if (migrations?.IsFailed == true)
        {
            return new OperateStatus("failed", FirstNonBlank(migrations.Message, "Database migrations failed."));
        }

        if (telemetry is not null && HasTelemetryFailure(telemetry))
        {
            return new OperateStatus("degraded", FirstNonBlank(telemetry.LastExportError, "One or more telemetry exporters are misconfigured."));
        }

        if (migrations?.UpgradeRequired == true)
        {
            return new OperateStatus("warning", "The server reports pending database migrations.");
        }

        if (recentErrors is not null && (recentErrors.Errors ?? []).Count > 0)
        {
            return new OperateStatus(
                "warning",
                $"{(recentErrors.Errors ?? []).Count.ToString(System.Globalization.CultureInfo.InvariantCulture)} recent server errors are retained in the admin buffer.");
        }

        if (version is not null || capabilities is not null || telemetry is not null || migrations is not null || recentErrors is not null)
        {
            return new OperateStatus("healthy", "Server overview endpoints responded with live admin data.");
        }

        if (versionFetch.Status == OperateSectionStatus.Forbidden
            || capabilitiesFetch.Status == OperateSectionStatus.Forbidden
            || telemetryFetch.Status == OperateSectionStatus.Forbidden
            || migrationsFetch.Status == OperateSectionStatus.Forbidden
            || errorsFetch.Status == OperateSectionStatus.Forbidden)
        {
            return new OperateStatus("unsupported", "The active profile cannot read one or more admin overview endpoints.");
        }

        return new OperateStatus("unknown", "The admin overview endpoints are unavailable.");
    }

    private static string ResolveOverviewLastSeen(
        ConsoleEnvironmentProfile profile,
        AdminVersionResponse? version,
        OperateTelemetryStatusResponse? telemetry,
        OperateMigrationStatusResponse? migrations,
        OperateRecentErrorsResponse? recentErrors)
    {
        var timestamps = new List<DateTimeOffset>
        {
            profile.UpdatedAt
        };
        AddIfPresent(timestamps, NonDefault(version?.ServerTime));
        AddIfPresent(timestamps, NonDefault(telemetry?.GeneratedAt));
        AddIfPresent(timestamps, NonDefault(migrations?.GeneratedAt));
        AddIfPresent(
            timestamps,
            NonDefault((recentErrors?.Errors ?? [])
                .Select(item => item.Timestamp)
                .Where(item => item != default)
                .DefaultIfEmpty()
                .Max()));

        return OperateObservabilityMapper.FormatTimestamp(timestamps.Max());
    }

    private static string ResolveOverviewDriftSummary(
        FetchResult<OperateMigrationStatusResponse> migrationsFetch,
        OperateMigrationStatusResponse? migrations,
        FetchResult<ConsoleApiEnvelope<AdminCapabilitiesResponse>> capabilitiesFetch,
        AdminCapabilitiesResponse? capabilities)
    {
        if (migrations is not null)
        {
            if (migrations.IsFailed)
            {
                return FirstNonBlank(migrations.Message, migrations.PlanError, "Migration drift check failed.");
            }

            if (migrations.UpgradeRequired)
            {
                return $"{(migrations.PendingScripts ?? []).Count.ToString(System.Globalization.CultureInfo.InvariantCulture)} pending migration scripts reported by the server.";
            }

            if (!migrations.PlanAvailable)
            {
                return FirstNonBlank(migrations.PlanError, "Migration plan is unavailable on this server.");
            }

            return "Server migration plan reports no pending drift.";
        }

        if (capabilities is not null)
        {
            return $"Server reports metadata API {DisplayValue(capabilities.MetadataApiVersion)} and schema {DisplayValue(capabilities.MetadataSchemaVersion)}.";
        }

        return FirstNonBlank(migrationsFetch.Message, capabilitiesFetch.Message, "Server drift metadata is unavailable.");
    }

    private static (string Version, string BuildSha) ResolveServerVersion(
        AdminVersionResponse? version,
        AdminCapabilitiesResponse? capabilities)
    {
        var rawVersion = FirstNonBlank(version?.Version, capabilities?.ServerVersion, "unknown");
        var buildSeparator = rawVersion.IndexOf('+', StringComparison.Ordinal);
        if (buildSeparator < 0)
        {
            return (rawVersion, "unknown");
        }

        var displayVersion = rawVersion[..buildSeparator];
        var buildSha = rawVersion[(buildSeparator + 1)..].Trim();
        if (buildSha.Length > 12)
        {
            buildSha = buildSha[..12];
        }

        return (string.IsNullOrWhiteSpace(displayVersion) ? "unknown" : displayVersion, string.IsNullOrWhiteSpace(buildSha) ? "unknown" : buildSha);
    }

    private static OperateStatus EnvelopeStatus<T>(FetchResult<ConsoleApiEnvelope<T>> fetch, string successDescription)
        where T : class
    {
        if (fetch.Ok && fetch.Value!.Success && fetch.Value.Data is not null)
        {
            return new OperateStatus("healthy", successDescription);
        }

        if (fetch.Ok)
        {
            return new OperateStatus("unknown", FirstNonBlank(fetch.Value!.Message, "The admin API returned an empty response envelope."));
        }

        return FetchStatus(fetch.Status, fetch.Message, successDescription);
    }

    private static OperateStatus FetchStatus(OperateSectionStatus status, string message, string successDescription) => status switch
    {
        OperateSectionStatus.Allowed => new OperateStatus("healthy", successDescription),
        OperateSectionStatus.Forbidden => new OperateStatus("unsupported", FirstNonBlank(message, "The active profile is not permitted to read this endpoint.")),
        OperateSectionStatus.Missing => new OperateStatus("unknown", FirstNonBlank(message, "The endpoint was not found on this server build.")),
        OperateSectionStatus.Unsupported => new OperateStatus("unsupported", FirstNonBlank(message, "The server does not advertise this capability.")),
        _ => new OperateStatus("unknown", FirstNonBlank(message, "The endpoint is unavailable."))
    };

    private static OperateStatus SubresourceStatus(OperateSectionStatus status, string message, string successDescription) => status switch
    {
        OperateSectionStatus.Allowed => new OperateStatus("healthy", successDescription),
        OperateSectionStatus.Forbidden => new OperateStatus("forbidden", FirstNonBlank(message, "The active profile is not permitted to read this sub-resource.")),
        OperateSectionStatus.Missing => new OperateStatus("missing", FirstNonBlank(message, "The sub-resource was not found on this server build.")),
        OperateSectionStatus.Unsupported => new OperateStatus("unsupported", FirstNonBlank(message, "The server does not advertise this sub-resource.")),
        _ => new OperateStatus("unknown", FirstNonBlank(message, "The sub-resource is unavailable."))
    };

    private static OperateStatus ExportStatus(string signal, bool enabled, string state, string? lastError)
    {
        if (!enabled)
        {
            return new OperateStatus("disabled", $"{signal} export is disabled by the server telemetry status.");
        }

        var normalized = NormalizeServerState(state);
        var description = $"{signal} export state reported as {normalized}.";
        if (!string.IsNullOrWhiteSpace(lastError) && IsTelemetryFailureState(normalized))
        {
            description = $"{description} Last exporter error: {lastError}";
        }

        return new OperateStatus(normalized, description);
    }

    private static OperateStatus RealtimeStatus(OperateRealtimeStatusResponse realtime)
    {
        if (!realtime.Supported)
        {
            return new OperateStatus("unsupported", "Admin realtime status is not supported by this server.");
        }

        var events = (realtime.Events ?? []).Length == 0 ? "no events advertised" : string.Join(", ", realtime.Events ?? []);
        return new OperateStatus(
            "healthy",
            $"Admin realtime uses {DisplayValue(realtime.Protocol)} at {DisplayValue(realtime.HubPath)} with {events}.");
    }

    private static OperateStatus MigrationStatus(OperateMigrationStatusResponse migrations)
    {
        if (migrations.IsFailed)
        {
            return new OperateStatus("failed", FirstNonBlank(migrations.Message, migrations.PlanError, "Database migrations failed."));
        }

        if (migrations.UpgradeRequired)
        {
            return new OperateStatus(
                "warning",
                $"{(migrations.PendingScripts ?? []).Count.ToString(System.Globalization.CultureInfo.InvariantCulture)} pending migration scripts reported by the server.");
        }

        if (!migrations.PlanAvailable)
        {
            return new OperateStatus("unknown", FirstNonBlank(migrations.PlanError, "Migration planning is unavailable."));
        }

        return new OperateStatus(NormalizeServerState(migrations.Status), FirstNonBlank(migrations.Message, "Migration status returned by honua-server."));
    }

    private static OperateStatus RecentErrorsStatus(OperateRecentErrorsResponse recentErrors)
    {
        var bufferedErrors = recentErrors.Errors ?? [];
        if (bufferedErrors.Count == 0)
        {
            return new OperateStatus("healthy", $"Recent error buffer {DisplayValue(recentErrors.InstanceId)} has no retained errors.");
        }

        var newest = bufferedErrors.OrderByDescending(item => item.Timestamp).First();
        return new OperateStatus(
            "warning",
            $"Newest retained error is HTTP {newest.StatusCode.ToString(System.Globalization.CultureInfo.InvariantCulture)} at {newest.Path}.");
    }

    private static OperateStatus TelemetryContractStatus(OperateTelemetryStatusResponse telemetry)
    {
        if (HasTelemetryFailure(telemetry))
        {
            return new OperateStatus("degraded", FirstNonBlank(telemetry.LastExportError, "One or more telemetry exporters are misconfigured."));
        }

        if (!telemetry.TracingEnabled && !telemetry.MetricsEnabled && !telemetry.LogsEnabled)
        {
            return new OperateStatus("disabled", "Tracing, metrics, and log export are disabled.");
        }

        return new OperateStatus("healthy", "Telemetry export state returned by honua-server.");
    }

    private static bool HasTelemetryFailure(OperateTelemetryStatusResponse telemetry) =>
        IsTelemetryFailureState(NormalizeServerState(telemetry.OtlpExporterState))
        || IsTelemetryFailureState(NormalizeServerState(telemetry.TraceExportState))
        || IsTelemetryFailureState(NormalizeServerState(telemetry.MetricsExportState))
        || IsTelemetryFailureState(NormalizeServerState(telemetry.LogExportState));

    private static bool IsTelemetryFailureState(string state) =>
        state is "misconfigured" or "failed" or "failing" or "unhealthy";

    private static string NormalizeServerState(string? state)
    {
        if (string.IsNullOrWhiteSpace(state))
        {
            return "unknown";
        }

        return state.Trim() switch
        {
            "notConfigured" => "not configured",
            "not_configured" => "not configured",
            "not-configured" => "not configured",
            var value => value.Replace("_", " ", StringComparison.Ordinal).Replace("-", " ", StringComparison.Ordinal).ToLowerInvariant()
        };
    }

    private static string FormatFreshness(DateTimeOffset? value) =>
        value is { } resolved ? OperateObservabilityMapper.FormatTimestamp(resolved) : "unknown";

    private static DateTimeOffset? NonDefault(DateTimeOffset? value) =>
        value is { } resolved && resolved != default ? resolved : null;

    private static void AddIfPresent(ICollection<DateTimeOffset> values, DateTimeOffset? value)
    {
        if (value is { } resolved)
        {
            values.Add(resolved);
        }
    }

    private static string DisplayValue(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "unknown" : value.Trim();

    private static string FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private static OperateAlertRule MapRuleWithHealth(AlertRuleResponse rule, RuleHealthResult? health)
    {
        health ??= RuleHealthResult.Denied(
            OperateSectionStatus.Unavailable,
            "Rule health was not resolved by the server.");

        if (health.Status == OperateSectionStatus.Allowed && health.Health is not null)
        {
            return OperateObservabilityMapper.MapRule(rule, health.Health);
        }

        var mapped = OperateObservabilityMapper.MapRule(rule, health.Health);
        var status = SubresourceStatus(
            health.Status,
            health.Message,
            "Rule health returned by honua-server.");
        var message = $"Rule health unavailable: {FirstNonBlank(health.Message, OperateSectionPresentation.FallbackMessage(health.Status))}";
        return mapped with
        {
            Status = status,
            ValidationMessages = mapped.ValidationMessages
                .Concat([message])
                .ToArray()
        };
    }

    private async Task<IReadOnlyDictionary<long, RuleHealthResult>> ResolveRuleHealthAsync(
        IReadOnlyList<AlertRuleResponse> rules,
        CancellationToken cancellationToken)
    {
        if (rules.Count == 0)
        {
            return new Dictionary<long, RuleHealthResult>();
        }

        var resolved = await BoundedFanOutAsync(
            rules,
            async rule =>
            {
                var health = await FetchAsync(
                    OperateAdminRoutes.RuleHealth(rule.RuleId),
                    OperateObservabilityJsonContext.Default.AlertRuleHealthEnvelope,
                    cancellationToken).ConfigureAwait(false);
                if (health.Ok && health.Value!.Success && health.Value.Data is not null)
                {
                    return (rule.RuleId, Result: RuleHealthResult.Allowed(health.Value.Data));
                }

                var status = health.Ok ? OperateSectionStatus.Unavailable : health.Status;
                var message = health.Ok
                    ? FirstNonBlank(health.Value!.Message, "The rule health endpoint returned an empty response envelope.")
                    : health.Message;
                return (rule.RuleId, Result: RuleHealthResult.Denied(status, message));
            },
            cancellationToken).ConfigureAwait(false);
        return resolved
            .ToDictionary(entry => entry.RuleId, entry => entry.Result);
    }

    // Runs <paramref name="body"/> over every item with in-flight concurrency capped at
    // <see cref="MaxDetailFanoutConcurrency"/>, so a large list cannot open an unbounded number of
    // concurrent admin sockets while still avoiding a fully serial waterfall. The page cancellation token
    // is honored both while waiting for a slot and inside each body call.
    private static async Task<IReadOnlyList<TResult>> BoundedFanOutAsync<TItem, TResult>(
        IReadOnlyList<TItem> items,
        Func<TItem, Task<TResult>> body,
        CancellationToken cancellationToken)
    {
        using var gate = new SemaphoreSlim(MaxDetailFanoutConcurrency, MaxDetailFanoutConcurrency);
        var tasks = items.Select(async item =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await body(item).ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
            }
        });

        return await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<OperateInvestigation>> ResolveInvestigationDetailsAsync(
        InvestigationPageResponse page,
        CancellationToken cancellationToken)
    {
        var pageItems = page.Items ?? [];
        if (pageItems.Count == 0)
        {
            return [];
        }

        return await BoundedFanOutAsync(
            pageItems,
            async summary =>
            {
                var detail = await FetchAsync(
                    OperateAdminRoutes.InvestigationDetail(summary.InvestigationId),
                    OperateObservabilityJsonContext.Default.InvestigationResponse,
                    cancellationToken).ConfigureAwait(false);
                if (detail.Ok)
                {
                    return OperateObservabilityMapper.MapInvestigation(detail.Value!);
                }

                var projected = OperateObservabilityMapper.MapInvestigation(summary);
                var message = FirstNonBlank(
                    detail.Message,
                    "Investigation detail, pins, and linked resources are unavailable from the server.");
                return projected with
                {
                    DetailStatus = SubresourceStatus(
                        detail.Status,
                        message,
                        "Investigation detail returned by honua-server."),
                    DetailMessage = message,
                    Notes = projected.Notes
                        .Concat([$"Investigation detail unavailable: {message}"])
                        .ToArray()
                };
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<FetchResult<T>> FetchAsync<T>(
        string relativePath,
        JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken)
        where T : class
    {
        var profile = await _profileStore.GetActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            return FetchResult<T>.Failed(
                OperateSectionStatus.Unavailable,
                "No active environment profile is selected. Connect an environment to load Operate observability.",
                profile: null);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, BuildUri(profile.ServerBaseUri, relativePath));
        var token = await ResolveTokenAsync(profile, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        else if (!string.IsNullOrWhiteSpace(_adminApiKey))
        {
            // No account-session bearer token (e.g. the browser host has no interactive sign-in): fall back
            // to the configured admin API key like the GitOps/metrics clients, so the admin-authorized
            // observability endpoints return data instead of 401 Unauthorized.
            request.Headers.TryAddWithoutValidation("X-API-Key", _adminApiKey);
        }

        try
        {
            using var response = await _http
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return FetchResult<T>.Failed(
                    MapStatus(response.StatusCode),
                    $"The honua-server admin API returned {(int)response.StatusCode} {response.ReasonPhrase}.",
                    profile);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var value = await JsonSerializer.DeserializeAsync(stream, typeInfo, cancellationToken).ConfigureAwait(false);
            return value is null
                ? FetchResult<T>.Failed(OperateSectionStatus.Unavailable, "The honua-server admin API returned an empty response.", profile)
                : FetchResult<T>.Succeeded(value, profile);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException)
        {
            return FetchResult<T>.Failed(
                OperateSectionStatus.Unavailable,
                "The honua-server admin API is unreachable or returned an unreadable response.",
                profile);
        }
    }

    private async Task<FetchResult<T>> PostAsync<T>(
        string relativePath,
        JsonTypeInfo<T> typeInfo,
        string action,
        CancellationToken cancellationToken)
        where T : class
    {
        var profile = await _profileStore.GetActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            return FetchResult<T>.Failed(
                OperateSectionStatus.Unavailable,
                "No active environment profile is selected. Connect an environment to control Operate jobs.",
                profile: null);
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, BuildUri(profile.ServerBaseUri, relativePath));
        var token = await ResolveTokenAsync(profile, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        else if (!string.IsNullOrWhiteSpace(_adminApiKey))
        {
            request.Headers.TryAddWithoutValidation("X-API-Key", _adminApiKey);
        }

        // The control endpoints take no body; send an empty JSON object so the
        // request advertises application/json consistently with the other writes.
        request.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");

        try
        {
            using var response = await _http
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var body = await ReadBodySafelyAsync(response, cancellationToken).ConfigureAwait(false);
                return FetchResult<T>.Failed(
                    MapStatus(response.StatusCode),
                    MapControlErrorMessage(response.StatusCode, action, body),
                    profile);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var value = await JsonSerializer.DeserializeAsync(stream, typeInfo, cancellationToken).ConfigureAwait(false);
            return value is null
                ? FetchResult<T>.Failed(OperateSectionStatus.Unavailable, "The honua-server admin API returned an empty response.", profile)
                : FetchResult<T>.Succeeded(value, profile);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException)
        {
            return FetchResult<T>.Failed(
                OperateSectionStatus.Unavailable,
                "The honua-server admin API is unreachable or returned an unreadable response.",
                profile);
        }
    }

    private static async Task<string> ReadBodySafelyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return string.Empty;
        }
    }

    private static string MapControlErrorMessage(HttpStatusCode code, string action, string body) => code switch
    {
        // The server's approval gate returns 403 for both "approval required" and
        // plain "not permitted". The approval-required case carries an
        // application/problem+json body whose title is "Approval required"; detect
        // it so the operator sees that approval (not a missing role) is the blocker.
        HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized when IsApprovalRequired(body) =>
            "This action requires approval. The server's approval gate is holding it pending an approver; "
            + "it has not run.",
        HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized =>
            $"The active environment profile is not permitted to {action} this job.",
        HttpStatusCode.Conflict =>
            $"This job cannot be {(string.Equals(action, "retry", StringComparison.Ordinal) ? "retried" : "cancelled")} in its current state "
            + "(it may already be terminal, running past the cancellable point, or not in a retryable state).",
        HttpStatusCode.NotFound =>
            "The job was not found on the connected server.",
        HttpStatusCode.NotImplemented =>
            "The connected server does not expose the job control endpoints.",
        _ => $"The honua-server admin API returned {(int)code} {(string.IsNullOrWhiteSpace(action) ? "for the control action" : $"for the {action} action")}.",
    };

    private static bool IsApprovalRequired(string body) =>
        !string.IsNullOrWhiteSpace(body)
        && (body.Contains("approval-required", StringComparison.OrdinalIgnoreCase)
            || body.Contains("Approval required", StringComparison.OrdinalIgnoreCase));

    private Task<string?> ResolveTokenAsync(ConsoleEnvironmentProfile profile, CancellationToken cancellationToken) =>
        ConsoleServerHttp.ResolveForwardableBearerAsync(_sessionStore, profile, cancellationToken);

    private static Uri BuildUri(Uri baseUri, string relativePath) =>
        ConsoleServerHttp.BuildUri(baseUri, relativePath);

    private static OperateSectionStatus MapStatus(HttpStatusCode code) => code switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => OperateSectionStatus.Forbidden,
        HttpStatusCode.NotFound => OperateSectionStatus.Missing,
        HttpStatusCode.NotImplemented => OperateSectionStatus.Unsupported,
        _ => OperateSectionStatus.Unavailable
    };

    private sealed class FetchResult<T>
        where T : class
    {
        public OperateSectionStatus Status { get; private init; }

        public T? Value { get; private init; }

        public string Message { get; private init; } = string.Empty;

        public ConsoleEnvironmentProfile? Profile { get; private init; }

        public bool Ok => Status == OperateSectionStatus.Allowed && Value is not null;

        public static FetchResult<T> Succeeded(T value, ConsoleEnvironmentProfile profile) =>
            new() { Status = OperateSectionStatus.Allowed, Value = value, Profile = profile };

        public static FetchResult<T> Failed(OperateSectionStatus status, string message, ConsoleEnvironmentProfile? profile) =>
            new() { Status = status, Message = message, Profile = profile };
    }

    private sealed record RuleHealthResult(
        AlertRuleHealthResponse? Health,
        OperateSectionStatus Status,
        string Message)
    {
        public static RuleHealthResult Allowed(AlertRuleHealthResponse health) =>
            new(health, OperateSectionStatus.Allowed, string.Empty);

        public static RuleHealthResult Denied(OperateSectionStatus status, string message) =>
            new(null, status, message);
    }
}
