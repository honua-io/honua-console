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

    private readonly HttpClient _http;
    private readonly IConsoleEnvironmentProfileStore _profileStore;
    private readonly IConsoleAccountSessionStore _sessionStore;

    public HttpConsoleOperateObservabilityClient(
        HttpClient http,
        IConsoleEnvironmentProfileStore profileStore,
        IConsoleAccountSessionStore sessionStore)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _profileStore = profileStore ?? throw new ArgumentNullException(nameof(profileStore));
        _sessionStore = sessionStore ?? throw new ArgumentNullException(nameof(sessionStore));
    }

    public async Task<OperateSectionResult<OperateFleetOverview>> GetOverviewAsync(
        CancellationToken cancellationToken = default)
    {
        // A single lightweight probe makes the overview genuinely server-backed
        // without fanning out every section: it confirms the admin API
        // responds and classifies reachability. Forbidden/unavailable stay
        // neutral and never mark the environment failed (preserving #41).
        var probe = await FetchAsync(
            $"{OperateAdminRoutes.Events}?pageSize=1",
            OperateObservabilityJsonContext.Default.OperateEventPageResponse,
            cancellationToken).ConfigureAwait(false);

        if (probe.Profile is null)
        {
            return OperateSectionResult<OperateFleetOverview>.Denied(probe.Status, probe.Message);
        }

        var reachability = ProbeTelemetryStatus(probe.Status);
        var health = probe.Status == OperateSectionStatus.Allowed
            ? new OperateStatus("healthy", "The honua-server admin observability API responded.")
            : new OperateStatus("unknown", probe.Message);

        var environment = OperateObservabilityMapper.MapEnvironment(
            probe.Profile,
            health,
            "Bound to live honua-server /api/v1/admin observability via the Console contracts shim.");

        var overview = new OperateFleetOverview(
            Environments: [environment],
            TelemetryFacts:
            [
                new OperateTelemetryFact(probe.Profile.Id, probe.Profile.ServerBaseUri.Host, "Admin observability API", reachability, reachability.Label, OperateObservabilityRoutes.Observability),
                new OperateTelemetryFact(probe.Profile.Id, probe.Profile.ServerBaseUri.Host, "Operate SDK projection", new OperateStatus("not configured", "Bound through a thin HttpClient shim until honua-sdk-dotnet projects the Operate contracts."), "not configured", OperateObservabilityRoutes.Observability)
            ],
            CompatibilityRows:
            [
                new OperateCompatibilityRow(
                    "SDK contract projection",
                    "thin HttpClient shim",
                    "honua-sdk-dotnet Operate projection",
                    new OperateStatus("unknown", "Tracked as the SHIM swap target honua-sdk-dotnet#231."),
                    "Honua.Console.Contracts/OperateObservabilityContracts.cs")
            ]);

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
            .Select(rule => OperateObservabilityMapper.MapRule(rule, healthByRule.GetValueOrDefault(rule.RuleId)))
            .ToArray();

        var zonesFetch = await FetchAsync(
            OperateAdminRoutes.AlertZones,
            OperateObservabilityJsonContext.Default.AlertZoneListEnvelope,
            cancellationToken).ConfigureAwait(false);

        var zones = zonesFetch.Ok
            ? (zonesFetch.Value!.Data ?? []).Select(OperateObservabilityMapper.MapZone).ToArray()
            : [];

        return OperateSectionResult<OperateRulesView>.Allowed(new OperateRulesView(rules, zones));
    }

    public async Task<OperateSectionResult<IReadOnlyList<OperateJobRun>>> GetJobsAsync(
        CancellationToken cancellationToken = default)
    {
        var fetch = await FetchAsync(
            $"{OperateAdminRoutes.Jobs}?limit={DefaultPageSize}",
            OperateObservabilityJsonContext.Default.ConsoleJobListResponse,
            cancellationToken).ConfigureAwait(false);

        if (!fetch.Ok)
        {
            return OperateSectionResult<IReadOnlyList<OperateJobRun>>.Denied(fetch.Status, fetch.Message);
        }

        var jobs = fetch.Value!.Items
            .Select(summary => OperateObservabilityMapper.MapJobSummary(summary, fetch.Profile!))
            .ToArray();
        return OperateSectionResult<IReadOnlyList<OperateJobRun>>.Allowed(jobs);
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
            : (IReadOnlyList<string>)["Job logs are not available."];
        var artifacts = artifactsFetch.Ok
            ? OperateObservabilityMapper.MapJobArtifacts(artifactsFetch.Value!)
            : [];

        return OperateSectionResult<OperateJobRun>.Allowed(
            OperateObservabilityMapper.MapJobDetail(detailFetch.Value!, logs, artifacts, detailFetch.Profile!));
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

    private async Task<IReadOnlyDictionary<long, AlertRuleHealthResponse>> ResolveRuleHealthAsync(
        IReadOnlyList<AlertRuleResponse> rules,
        CancellationToken cancellationToken)
    {
        if (rules.Count == 0)
        {
            return new Dictionary<long, AlertRuleHealthResponse>();
        }

        var tasks = rules.Select(async rule =>
        {
            var health = await FetchAsync(
                OperateAdminRoutes.RuleHealth(rule.RuleId),
                OperateObservabilityJsonContext.Default.AlertRuleHealthEnvelope,
                cancellationToken).ConfigureAwait(false);
            return (rule.RuleId, Health: health.Ok ? health.Value!.Data : null);
        });

        var resolved = await Task.WhenAll(tasks).ConfigureAwait(false);
        return resolved
            .Where(entry => entry.Health is not null)
            .ToDictionary(entry => entry.RuleId, entry => entry.Health!);
    }

    private async Task<IReadOnlyList<OperateInvestigation>> ResolveInvestigationDetailsAsync(
        InvestigationPageResponse page,
        CancellationToken cancellationToken)
    {
        if (page.Items.Count == 0)
        {
            return [];
        }

        var tasks = page.Items.Select(async summary =>
        {
            var detail = await FetchAsync(
                OperateAdminRoutes.InvestigationDetail(summary.InvestigationId),
                OperateObservabilityJsonContext.Default.InvestigationResponse,
                cancellationToken).ConfigureAwait(false);
            return detail.Ok
                ? OperateObservabilityMapper.MapInvestigation(detail.Value!)
                : OperateObservabilityMapper.MapInvestigation(summary);
        });

        return await Task.WhenAll(tasks).ConfigureAwait(false);
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

    private async Task<string?> ResolveTokenAsync(ConsoleEnvironmentProfile profile, CancellationToken cancellationToken)
    {
        if (profile.Account.AuthMode == ConsoleAccountAuthMode.Anonymous)
        {
            return null;
        }

        var session = await _sessionStore.GetSessionAsync(profile.Id, cancellationToken).ConfigureAwait(false);
        return session?.AccessToken;
    }

    private static Uri BuildUri(Uri baseUri, string relativePath)
    {
        var normalizedBase = baseUri.AbsoluteUri.EndsWith('/')
            ? baseUri
            : new Uri(baseUri.AbsoluteUri + "/", UriKind.Absolute);
        return new Uri(normalizedBase, relativePath);
    }

    private static OperateSectionStatus MapStatus(HttpStatusCode code) => code switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => OperateSectionStatus.Forbidden,
        HttpStatusCode.NotFound => OperateSectionStatus.Missing,
        HttpStatusCode.NotImplemented => OperateSectionStatus.Unsupported,
        _ => OperateSectionStatus.Unavailable
    };

    private static OperateStatus ProbeTelemetryStatus(OperateSectionStatus status) => status switch
    {
        OperateSectionStatus.Allowed => new OperateStatus("healthy", "Admin observability API responded."),
        OperateSectionStatus.Forbidden => new OperateStatus("unsupported", "The active profile cannot read observability events."),
        OperateSectionStatus.Missing => new OperateStatus("unknown", "The observability events endpoint was not found on this server build."),
        _ => new OperateStatus("unknown", "The admin observability API did not respond.")
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
}
