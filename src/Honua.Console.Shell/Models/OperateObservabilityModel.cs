using Honua.Console.Shell.Services;

namespace Honua.Console.Shell.Models;

public static class OperateObservabilityRoutes
{
    public const string Root = "/operate";
    public const string Observability = "/operate/observability";

    public static string EventDetail(string eventId) => $"/operate/events/{Uri.EscapeDataString(eventId)}";

    public static string AlertDetail(string alertId) => $"/operate/alerts/{Uri.EscapeDataString(alertId)}";

    public static string JobDetail(string jobRunId) => $"/operate/jobs/{Uri.EscapeDataString(jobRunId)}";

    public const string Geoprocessing = "/operate/geoprocessing";

    public static string GeoprocessingJobDetail(string jobRunId) =>
        $"/operate/geoprocessing/{Uri.EscapeDataString(jobRunId)}";

    /// <summary>
    /// Deep-links into the evidence timeline pre-filtered to one correlation id (console#292
    /// correlation-id chip). Client-side query-string convenience over the existing events
    /// filter form (<c>OperateObservabilityPage</c>) — no new server endpoint.
    /// </summary>
    public static string CorrelationSearch(string correlationId) =>
        $"{Observability}?correlationId={Uri.EscapeDataString(correlationId)}#events";

    /// <summary>
    /// Deep-links into the Copilot Findings surface, anchored to one finding (console#292).
    /// Copilot Findings has no per-finding route; the page renders an anchor id per finding so
    /// this resolves without a new server endpoint.
    /// </summary>
    public static string FindingDetail(string findingId) =>
        $"/operate/copilot#finding-{Uri.EscapeDataString(findingId)}";

    /// <summary>
    /// Deep-links into the Approval inbox pre-selecting one proposal (console#292). Client-side
    /// query-string convenience over the inbox's existing selection state — no new server
    /// endpoint.
    /// </summary>
    public static string ProposalDetail(string proposalId) =>
        $"/inbox?proposalId={Uri.EscapeDataString(proposalId)}";

    /// <summary>
    /// Deep-links into the Deploy page pre-tracking one deploy-control operation (console#292).
    /// Client-side query-string convenience over the deploy page's existing "track operation"
    /// input — no new server endpoint (the server exposes operations by durable id only, no
    /// list-all endpoint).
    /// </summary>
    public static string OperationDetail(string operationId) =>
        $"/operate/deploy?operationId={Uri.EscapeDataString(operationId)}#deploy-approvals";
}

/// <summary>
/// The execution job kind wire values used as the <c>kind</c> filter on the
/// admin jobs endpoint. The server parses this against its <c>ExecutionJobKind</c>
/// enum (case-insensitive member name), so the value is the enum member name.
/// </summary>
public static class OperateJobKinds
{
    public const string Geoprocessing = "Geoprocessing";
}

/// <summary>
/// Helpers over the Operate job status vocabulary. The status state is the
/// server's <c>ExecutionJobStatus</c> name (Queued/Provisioning/Running/Succeeded/
/// Failed/Cancelled), surfaced verbatim on <see cref="OperateStatus.State"/>.
/// </summary>
public static class OperateJobStatuses
{
    public const string Queued = "Queued";
    public const string Provisioning = "Provisioning";
    public const string Running = "Running";
    public const string Succeeded = "Succeeded";
    public const string Failed = "Failed";
    public const string Cancelled = "Cancelled";

    /// <summary>
    /// A job in a terminal state no longer changes, so polling can stop once the
    /// open job reaches Succeeded/Failed/Cancelled.
    /// </summary>
    public static bool IsTerminal(string? state) =>
        string.Equals(state, Succeeded, StringComparison.OrdinalIgnoreCase)
        || string.Equals(state, Failed, StringComparison.OrdinalIgnoreCase)
        || string.Equals(state, Cancelled, StringComparison.OrdinalIgnoreCase);
}

public sealed record OperateObservabilitySnapshot(
    IReadOnlyList<OperateEnvironmentOverview> Environments,
    IReadOnlyList<OperateTelemetryFact> TelemetryFacts,
    IReadOnlyList<OperateCompatibilityRow> CompatibilityRows,
    IReadOnlyList<OperateEventRow> Events,
    IReadOnlyList<OperateAlertRecord> Alerts,
    IReadOnlyList<OperateAlertRule> Rules,
    IReadOnlyList<OperateJobRun> Jobs,
    IReadOnlyList<OperateInvestigation> Investigations)
{
    public static IReadOnlyList<string> RequiredJobSources { get; } =
    [
        "Studio",
        "Publishing",
        "GitOps",
        "Temporal",
        "Alert delivery",
        "Import",
        "Maintenance"
    ];

    public static OperateObservabilitySnapshot Empty { get; } =
        new([], [], [], [], [], [], [], []);

    public OperateEventRow SelectedEvent(string? eventId) =>
        FindEvent(eventId) ?? Events.First();

    public OperateEventRow? FindEvent(string? eventId) =>
        string.IsNullOrWhiteSpace(eventId)
            ? null
            : Events.FirstOrDefault(item => string.Equals(item.EventId, eventId, StringComparison.OrdinalIgnoreCase));

    public OperateAlertRecord SelectedAlert(string? alertId) =>
        FindAlert(alertId) ?? Alerts.First();

    public OperateAlertRecord? FindAlert(string? alertId) =>
        string.IsNullOrWhiteSpace(alertId)
            ? null
            : Alerts.FirstOrDefault(item => string.Equals(item.AlertId, alertId, StringComparison.OrdinalIgnoreCase));

    public OperateJobRun SelectedJob(string? jobRunId) =>
        FindJob(jobRunId) ?? Jobs.First();

    public OperateJobRun? FindJob(string? jobRunId) =>
        string.IsNullOrWhiteSpace(jobRunId)
            ? null
            : Jobs.FirstOrDefault(item => string.Equals(item.JobRunId, jobRunId, StringComparison.OrdinalIgnoreCase));

    public bool HasUnifiedJobDeepLinks =>
        RequiredJobSources.All(source => Jobs.Any(job =>
            string.Equals(job.Source, source, StringComparison.OrdinalIgnoreCase)
            && job.DetailHref.StartsWith("/operate/jobs/", StringComparison.Ordinal)));
}

/// <summary>
/// The single status-vocabulary mapping table for the platform's status space (console#293):
/// overallStatus / SLO flags (ops health), finding-state style raw strings, deploy/workflow
/// lifecycle states (including <c>ManualInterventionRequired</c>), and proposal lifecycle states
/// all normalize onto one of five visual buckets here — success / info / warning / danger /
/// neutral — via <see cref="CssClass"/>. Render it through the shared <c>OperateStatusPill</c>
/// component rather than re-deriving a CSS class inline.
///
/// Enum-typed domains (e.g. <c>ConsoleProposalStatus</c>, <c>DeployOperationLifecycle</c>) keep
/// their own label helpers (the words differ per domain) but delegate their CSS-class mapping to
/// this record's <see cref="CssClass"/> so "warning" always looks the same regardless of which
/// surface produced it — see <c>ConsoleProposalPresentation.ToStatus</c> /
/// <c>DeployOperationPresentation.ToStatus</c>.
///
/// An unrecognized raw state (including a future finding state this table has not seen yet)
/// falls through to <see cref="IsNeutral"/>'s default — neutral — rather than guessing success or
/// danger; this is the safe default for any raw server string, not just the words listed below.
/// </summary>
public sealed record OperateStatus(string State, string Description)
{
    private static readonly HashSet<string> NeutralStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "unknown",
        "unsupported",
        "missing",
        "disabled",
        "not configured",
        "unconfigured"
    };

    private static readonly HashSet<string> FailureStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "critical",
        "error",
        "failed",
        "failing",
        "firing",
        "invalid",
        "misconfigured",
        "unhealthy",
        "blocked",
        // Proposal / deploy-operation lifecycle failure states (console#293 unification).
        "rejected",
        "manual intervention required"
    };

    public string NormalizedState => NormalizeState(State);

    public string Label => NormalizedState switch
    {
        "missing" => "unknown",
        "unconfigured" => "not configured",
        _ => NormalizedState
    };

    public bool IsNeutral => NeutralStates.Contains(NormalizedState);

    public bool IsFailure => FailureStates.Contains(NormalizedState);

    /// <summary>
    /// Whether this status renders as a warning or danger badge (console#292): the shared test
    /// for "this badge is a breach an operator should act on" used to decide whether a health
    /// section renders a deep link to its actionable surface. Neutral/success/info states are
    /// not breaches; a badge for those is never a dead end because there is nothing to act on.
    /// </summary>
    public bool IsBreach => CssClass is "console-state-warning" or "console-state-danger";

    public string CssClass => NormalizedState switch
    {
        "configured" or "healthy" or "succeeded" or "resolved" or "valid" => "console-state-success",
        "running" or "info" or "notice" or "submitted" or "reconciling" => "console-state-info",
        "warning" or "degraded" or "acknowledged" or "retrying" or "waiting"
            // Proposal / deploy-operation lifecycle warning states (console#293 unification).
            or "awaiting approval" or "rolled back" or "rollback requested" => "console-state-warning",
        _ when IsNeutral => "console-state-neutral",
        _ when IsFailure => "console-state-danger",
        _ => "console-state-neutral"
    };

    private static string NormalizeState(string state) =>
        state.Trim().ToLowerInvariant().Replace("_", " ");
}

public sealed record OperateEnvironmentOverview(
    string EnvironmentId,
    string Name,
    string ServerName,
    string Version,
    string BuildSha,
    OperateStatus Health,
    string LastSeen,
    string OwnerTeam,
    string DriftSummary);

public sealed record OperateTelemetryFact(
    string EnvironmentId,
    string ServerId,
    string Signal,
    OperateStatus State,
    string Freshness,
    string EvidenceHref)
{
    public bool IsNeutralTelemetry => State.IsNeutral;
}

public sealed record OperateCompatibilityRow(
    string Capability,
    string Current,
    string Required,
    OperateStatus State,
    string Evidence);

public sealed record OperateRelatedObject(
    string Kind,
    string Label,
    string Href);

public sealed record OperateEvidenceLink(
    string Kind,
    string Label,
    string Href,
    string Detail);

public sealed record OperateAiAdvisory(
    string Summary,
    IReadOnlyList<OperateEvidenceLink> EvidenceLinks,
    IReadOnlyList<string> SuggestedActions)
{
    public bool IsEvidenceLinked => EvidenceLinks.Count > 0;
}

public sealed record OperateEventFilter(
    string EnvironmentId,
    string EventType,
    string CorrelationId)
{
    public static OperateEventFilter Empty { get; } = new(string.Empty, string.Empty, string.Empty);

    public bool Matches(OperateEventRow item) =>
        MatchesOptional(EnvironmentId, item.EnvironmentId)
        && MatchesOptional(EventType, item.EventType)
        && MatchesContains(CorrelationId, item.CorrelationId);

    private static bool MatchesOptional(string filterValue, string value) =>
        string.IsNullOrWhiteSpace(filterValue)
        || string.Equals(filterValue.Trim(), value, StringComparison.OrdinalIgnoreCase);

    private static bool MatchesContains(string filterValue, string value) =>
        string.IsNullOrWhiteSpace(filterValue)
        || value.Contains(filterValue.Trim(), StringComparison.OrdinalIgnoreCase);
}

public sealed record OperateEventRow(
    string EventId,
    string EventTime,
    string Severity,
    string EventType,
    string Category,
    string Message,
    string EnvironmentId,
    string ServerId,
    string CorrelationId,
    string TraceId,
    string RequestId,
    string? JobRunId,
    string? AlertId,
    IReadOnlyList<OperateEvidenceLink> RawEvidence,
    IReadOnlyList<OperateRelatedObject> RelatedObjects,
    IReadOnlyList<string> Lifecycle,
    OperateAiAdvisory? AiAdvisory)
{
    public string DetailHref => OperateObservabilityRoutes.EventDetail(EventId);

    public OperateStatus SeverityStatus => new(Severity, Message);

    public bool PreservesRawEvidenceWithAi => AiAdvisory is null || RawEvidence.Count > 0;
}

public sealed record OperateAlertRecord(
    string AlertId,
    string Title,
    string Severity,
    OperateStatus Status,
    string Source,
    string OwnerTeam,
    string StartedAt,
    string LastSeenAt,
    IReadOnlyList<string> AffectedResources,
    IReadOnlyList<OperateEvidenceLink> EvidenceLinks,
    OperateAiAdvisory? AiAdvisory,
    IReadOnlyList<OperateAlertAction>? Actions = null)
{
    public string DetailHref => OperateObservabilityRoutes.AlertDetail(AlertId);

    public IReadOnlyList<OperateAlertAction> LifecycleActions => Actions ?? [];

    public bool PreservesRawEvidenceWithAi => AiAdvisory is null || EvidenceLinks.Count > 0;
}

public sealed record OperateAlertAction(string Label, bool IsAllowed, string Reason);

public sealed record OperateGeofenceZone(
    string ZoneId,
    string Name,
    string ServiceId,
    bool Active,
    int Srid,
    string GeometrySummary,
    IReadOnlyList<string> Metadata);

public sealed record OperateAlertRule(
    string RuleId,
    string Name,
    string RuleType,
    bool Enabled,
    OperateStatus Status,
    string ConditionSummary,
    string DeliverySummary,
    string LastEvaluatedAt,
    int ActiveIncidentCount,
    int DeliveryFailureCount,
    IReadOnlyList<string> ValidationMessages,
    int RecentTriggerCount = 0,
    string LastTriggeredAt = "")
{
    public bool IsValid => !string.Equals(Status.NormalizedState, "invalid", StringComparison.OrdinalIgnoreCase)
        && ValidationMessages.Count == 0;

    public bool CanEnable => !Enabled && IsValid;

    public string EnableBlockReason =>
        Enabled ? "Already enabled"
        : CanEnable ? "Ready to enable"
        : string.Join("; ", ValidationMessages);
}

public sealed record OperateJobStage(
    string Name,
    OperateStatus Status,
    int ProgressPercent,
    string Detail);

public sealed record OperateJobMetric(
    string Name,
    string Value,
    OperateStatus Status);

/// <summary>
/// One sanitized step of a job's per-step glass-box (honua-server #2182):
/// its ordinal, phase, status, timeline, the sanitized provider command
/// (already <c>&lt;scratch&gt;</c>/<c>&lt;path&gt;</c>-redacted server-side),
/// produced artifacts, and metadata. <see cref="Command"/> is rendered verbatim;
/// the Console must not re-sanitize it.
/// </summary>
public sealed record OperateJobStep(
    int Ordinal,
    string Phase,
    OperateStatus Status,
    string Timing,
    string Duration,
    string Message,
    string Command,
    IReadOnlyList<OperateJobStepArtifact> Artifacts,
    IReadOnlyList<OperateJobStepMetadata> Metadata)
{
    public bool HasCommand => !string.IsNullOrWhiteSpace(Command);
}

public sealed record OperateJobStepArtifact(
    string Label,
    string Kind,
    string Size);

public sealed record OperateJobStepMetadata(
    string Key,
    string Value);

/// <summary>
/// The per-step glass-box view for a single job, surfaced through the shared
/// <see cref="OperateSectionResult{T}"/> envelope so the panel degrades through
/// the standard loading/forbidden/unavailable surfaces.
/// </summary>
public sealed record OperateJobStepsView(
    string JobRunId,
    string CorrelationId,
    OperateStatus State,
    IReadOnlyList<OperateJobStep> Steps)
{
    public static OperateJobStepsView Empty { get; } = new(string.Empty, string.Empty, new OperateStatus("unknown", string.Empty), []);
}

public sealed record OperateJobAction(
    string Label,
    bool IsAllowed,
    string Reason);

public sealed record OperateJobRun(
    string JobRunId,
    string Source,
    string JobType,
    string Queue,
    OperateStatus Status,
    string SubmittedBy,
    string SubmittedAt,
    string EnvironmentId,
    string ServerId,
    int ProgressPercent,
    string FailureClassification,
    IReadOnlyList<string> ResourceRefs,
    IReadOnlyList<OperateJobStage> Stages,
    IReadOnlyList<string> Logs,
    IReadOnlyList<OperateEvidenceLink> Artifacts,
    IReadOnlyList<OperateJobMetric> Metrics,
    IReadOnlyList<OperateJobAction> AllowedActions,
    IReadOnlyList<OperateRelatedObject> RelatedObjects,
    OperateSectionStatus LogsStatus = OperateSectionStatus.Allowed,
    string LogsMessage = "",
    OperateSectionStatus ArtifactsStatus = OperateSectionStatus.Allowed,
    string ArtifactsMessage = "")
{
    public string DetailHref => OperateObservabilityRoutes.JobDetail(JobRunId);

    public string ArtifactCountLabel =>
        Metrics.FirstOrDefault(metric => string.Equals(metric.Name, "Artifacts", StringComparison.OrdinalIgnoreCase))?.Value
        ?? Artifacts.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public bool LogsAllowed => LogsStatus == OperateSectionStatus.Allowed;

    public bool ArtifactsAllowed => ArtifactsStatus == OperateSectionStatus.Allowed;

    /// <summary>
    /// Cancel is offered while the job is non-terminal. The server's Execute +
    /// destructive-approval gate is the real authority and 403s a denied cancel;
    /// this only decides whether to surface the affordance.
    /// </summary>
    public bool CanCancel => !OperateJobStatuses.IsTerminal(Status.State);

    /// <summary>
    /// Retry is offered only for Failed/Cancelled jobs, matching the server's
    /// retry contract (other states return 409 Conflict).
    /// </summary>
    public bool CanRetry =>
        string.Equals(Status.State, OperateJobStatuses.Failed, StringComparison.OrdinalIgnoreCase)
        || string.Equals(Status.State, OperateJobStatuses.Cancelled, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Outcome of a durable-job control action (cancel/retry) projected from the
/// server's control response. <see cref="Status"/> is the job's resulting state
/// so the surface can refresh and reflect the transition.
/// </summary>
public sealed record OperateJobControlOutcome(
    string JobRunId,
    string Action,
    string Status,
    string Message,
    string CorrelationId);

public static class OperateActionPresentation
{
    // Action gating is now server-driven (ConsoleJobActionDescriptor.Allowed,
    // alert lifecycle state, rule health). Execution of mutating actions is a
    // follow-on slice; allowed actions render disabled with this note so an
    // operator can see the action is permitted but not yet wired in Console.
    public const string ActionExecutionDeferredReason = "Allowed by the server; mutating actions are wired in a follow-on Console slice.";

    public const string ActionApiUnavailableReason = ActionExecutionDeferredReason;
}

public sealed record OperateInvestigation(
    string InvestigationId,
    string Title,
    OperateStatus Status,
    string Owner,
    string TimeRange,
    IReadOnlyList<string> PinnedEventIds,
    IReadOnlyList<string> LinkedAlertIds,
    IReadOnlyList<string> LinkedJobRunIds,
    IReadOnlyList<string> Notes,
    OperateStatus? DetailStatus = null,
    string DetailMessage = "")
{
    public OperateStatus EffectiveDetailStatus => DetailStatus ?? new("healthy", "Investigation detail loaded.");

    public bool HasIncompleteDetails => EffectiveDetailStatus.IsFailure
        || EffectiveDetailStatus.IsNeutral
        || string.Equals(EffectiveDetailStatus.NormalizedState, "forbidden", StringComparison.OrdinalIgnoreCase)
        || string.Equals(EffectiveDetailStatus.NormalizedState, "warning", StringComparison.OrdinalIgnoreCase)
        || string.Equals(EffectiveDetailStatus.NormalizedState, "degraded", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Transient in-memory placeholder snapshot for the Operate observability surface.
/// </summary>
/// <remarks>
/// <para>
/// <b>Do not promote to a merged data source.</b> Environments, telemetry facts, events,
/// alerts, alert rules, jobs, and investigations are all server-owned. Per
/// <c>docs/migration/CONSOLE_PATTERNS_CHARTER.md</c> section 11, this fixture is a scaffold
/// that lets the Operate UX, models, and tests be built ahead of the
/// <c>honua-server</c>/<c>honua-sdk-dotnet</c> projection (tracked in honua-server#1168-1170
/// and honua-sdk-js#229). The surface stays blocked until that contract lands; this fixture
/// must never be the merged data source for the deployed surface.
/// </para>
/// <para>
/// A future server-backed replacement MUST preserve the invariants the Operate models and
/// tests assert against this snapshot, notably
/// <see cref="OperateObservabilitySnapshot.HasUnifiedJobDeepLinks"/> (every required job
/// source resolves to a <c>/operate/jobs/</c> deep link) and the neutral/failure state
/// normalization in <see cref="OperateStatus"/>.
/// </para>
/// </remarks>
public static class OperateObservabilityFixture
{
    public static OperateObservabilitySnapshot Default { get; } = new(
        Environments:
        [
            new(
                "prod",
                "Production",
                "honua-prod-01",
                "2026.05.18",
                "8f43c91",
                new("degraded", "One alert is firing; telemetry export is partially configured."),
                "2026-05-24 09:34 HST",
                "Platform Ops",
                "2 package bindings drifted from release rel-2026.05.18"),
            new(
                "staging",
                "Staging",
                "honua-staging-02",
                "2026.05.22-rc2",
                "b7315fa",
                new("unknown", "Server health probe has not reported since the environment profile was imported."),
                "unknown",
                "Platform Ops",
                "Compatibility state unknown until telemetry is configured")
        ],
        TelemetryFacts:
        [
            new("prod", "honua-prod-01", "Metrics", new("healthy", "OTLP metrics were received in the last five minutes."), "5 minutes", "/operate/events/evt-telemetry-001"),
            new("prod", "honua-prod-01", "Traces", new("unknown", "Trace freshness is not reported by this server build."), "unknown", "/operate/events/evt-telemetry-001"),
            new("prod", "honua-prod-01", "Logs", new("disabled", "Structured log export is disabled by operator policy."), "disabled", "/operate/events/evt-log-410"),
            new("prod", "honua-prod-01", "OTLP exporter", new("not configured", "No OTLP endpoint is configured for this environment."), "not configured", "/operate/events/evt-telemetry-001"),
            new("staging", "honua-staging-02", "Admin realtime", new("unsupported", "The connected server advertises no admin realtime capability."), "unsupported", "/operate/events/evt-telemetry-004"),
            new("staging", "honua-staging-02", "Replica metrics", new("missing", "Replica telemetry was not included in the server status response."), "unknown", "/operate/events/evt-telemetry-004")
        ],
        CompatibilityRows:
        [
            new("SDK contract projection", "mock UI projection", "honua-sdk-dotnet operate contract", new("unknown", "The backend/SDK projection is tracked in honua-server#1168-1170 and honua-sdk-js#229."), "docs/architecture/operate-observability-information-model.md"),
            new("Admin realtime stream", "not advertised", "optional", new("unsupported", "Realtime is not required for static evidence review."), "evt-telemetry-004"),
            new("OTLP exporter", "not configured", "optional", new("not configured", "Operators can still inspect product-level events and raw evidence links."), "evt-telemetry-001"),
            new("Package binding compatibility", "2 drifted", "0 drifted", new("degraded", "Generated app and layer package bindings differ from release rel-2026.05.18."), "evt-release-211")
        ],
        Events:
        [
            new(
                "evt-alert-900",
                "2026-05-24 09:31 HST",
                "critical",
                "alert",
                "release gate",
                "Publish SLO burn alert is firing for parcels service.",
                "prod",
                "honua-prod-01",
                "corr-rel-20260524",
                "trace-9b52",
                "req-publish-77",
                "job-publish-001",
                "alert-slo-burn",
                [
                    new("grafana", "SLO burn panel", "https://grafana.example.invalid/d/slo-parcels", "Error budget burn exceeded 2 percent over 30 minutes."),
                    new("log", "Release gate log", "/operate/events/evt-log-410", "Gate watcher recorded three failed package preview checks.")
                ],
                [
                    new("alert", "alert-slo-burn", "/operate/alerts/alert-slo-burn"),
                    new("job", "job-publish-001", "/operate/jobs/job-publish-001"),
                    new("release", "rel-2026.05.18", "/operate/events/evt-release-211")
                ],
                [
                    "Alert created from release gate rule",
                    "Delivery sent to ops-oncall",
                    "Investigation inv-prod-17 pinned the event"
                ],
                new(
                    "The alert correlates to the publish job and two generated-app preview failures. Raw logs and the SLO panel remain the source of truth.",
                    [
                        new("event", "Publish job failure", "/operate/events/evt-job-301", "Same correlation id corr-rel-20260524."),
                        new("grafana", "SLO burn panel", "https://grafana.example.invalid/d/slo-parcels", "Metric evidence linked from the alert.")
                    ],
                    [
                        "Review package binding drift before retrying the publish job.",
                        "Keep the release gate active until the SLO panel returns below threshold."
                    ])),
            new(
                "evt-job-301",
                "2026-05-24 09:29 HST",
                "error",
                "job",
                "publishing",
                "Publishing job failed while rendering generated app preview.",
                "prod",
                "honua-prod-01",
                "corr-rel-20260524",
                "trace-9b52",
                "req-publish-77",
                "job-publish-001",
                "alert-slo-burn",
                [
                    new("log", "Job runner log", "/operate/jobs/job-publish-001", "Stage preview failed after package binding validation."),
                    new("artifact", "Preview failure report", "/operate/jobs/job-publish-001#artifacts", "Failure report captured by the job runner.")
                ],
                [
                    new("job", "job-publish-001", "/operate/jobs/job-publish-001"),
                    new("catalog item", "parcels-service", "/catalog/parcels-service")
                ],
                [
                    "Job queued",
                    "Package validation passed with warnings",
                    "Preview stage failed"
                ],
                new(
                    "The failed stage is isolated to generated app preview. Publish state was not promoted.",
                    [
                        new("job", "Job stage log", "/operate/jobs/job-publish-001", "Stage log includes the binding validation warning."),
                        new("event", "Release gate alert", "/operate/events/evt-alert-900", "Alert fired from the same correlation id.")
                    ],
                    [
                        "Open the artifact report before retrying.",
                        "Use retry only after the package binding warning is resolved."
                    ])),
            new(
                "evt-audit-118",
                "2026-05-24 09:12 HST",
                "notice",
                "audit",
                "identity",
                "Operator added alert delivery channel ops-oncall.",
                "prod",
                "honua-prod-01",
                "corr-channel-18",
                "trace-661b",
                "req-admin-31",
                null,
                null,
                [
                    new("audit", "Audit record", "https://siem.example.invalid/audit/evt-audit-118", "Immutable audit entry with actor and RBAC decision.")
                ],
                [
                    new("user", "alex@honua.example", "/operate/events/evt-audit-118"),
                    new("channel", "ops-oncall", "/operate/alerts/alert-slo-burn")
                ],
                [
                    "Audit event written",
                    "SIEM export queued"
                ],
                null),
            new(
                "evt-release-211",
                "2026-05-24 08:58 HST",
                "warning",
                "release",
                "gitops",
                "GitOps release detected package binding drift before deployment.",
                "prod",
                "honua-prod-01",
                "corr-rel-20260524",
                "trace-f73a",
                "req-gitops-52",
                "job-gitops-001",
                null,
                [
                    new("ci", "Release check run", "https://ci.example.invalid/runs/rel-2026.05.18", "Compatibility check surfaced two drifted bindings."),
                    new("diff", "Compatibility diff", "/operate/jobs/job-gitops-001#artifacts", "Diff artifact is attached to the GitOps job.")
                ],
                [
                    new("job", "job-gitops-001", "/operate/jobs/job-gitops-001"),
                    new("release", "rel-2026.05.18", "/operate/events/evt-release-211")
                ],
                [
                    "Proposal merged",
                    "Compatibility check warning",
                    "Deploy held for operator review"
                ],
                null),
            new(
                "evt-sync-044",
                "2026-05-24 08:42 HST",
                "warning",
                "sync",
                "replica",
                "Disconnected replica has three conflicts waiting for review.",
                "prod",
                "honua-prod-01",
                "corr-replica-kona",
                "trace-sync-22",
                "req-sync-14",
                "job-maint-001",
                null,
                [
                    new("sync", "Replica conflict report", "/operate/jobs/job-maint-001#artifacts", "Conflict report includes layer ids and editor notes.")
                ],
                [
                    new("job", "job-maint-001", "/operate/jobs/job-maint-001"),
                    new("replica", "kona-field-team", "/operate/events/evt-sync-044")
                ],
                [
                    "Replica synchronized",
                    "Conflicts detected",
                    "Maintenance job scheduled"
                ],
                null),
            new(
                "evt-data-071",
                "2026-05-24 08:23 HST",
                "info",
                "data change",
                "temporal",
                "Temporal diff completed for zoning layer.",
                "staging",
                "honua-staging-02",
                "corr-temporal-zoning",
                "trace-temp-88",
                "req-temporal-64",
                "job-temporal-001",
                null,
                [
                    new("artifact", "Temporal diff summary", "/operate/jobs/job-temporal-001#artifacts", "Summary includes added, removed, and changed feature counts.")
                ],
                [
                    new("job", "job-temporal-001", "/operate/jobs/job-temporal-001"),
                    new("change set", "cs-zoning-20260524", "/operate/events/evt-data-071")
                ],
                [
                    "Snapshot selected",
                    "Diff executed",
                    "Artifact attached"
                ],
                null),
            new(
                "evt-log-410",
                "2026-05-24 09:28 HST",
                "error",
                "log",
                "generated app preview",
                "Preview renderer returned binding unsupported for dashboard package.",
                "prod",
                "honua-prod-01",
                "corr-rel-20260524",
                "trace-9b52",
                "req-publish-77",
                "job-publish-001",
                "alert-slo-burn",
                [
                    new("log", "Provider log", "https://logs.example.invalid/query?trace=trace-9b52", "Raw provider log line with exception digest."),
                    new("trace", "Trace span", "https://traces.example.invalid/trace-9b52", "Span attributes identify the unsupported binding.")
                ],
                [
                    new("job", "job-publish-001", "/operate/jobs/job-publish-001"),
                    new("alert", "alert-slo-burn", "/operate/alerts/alert-slo-burn")
                ],
                [
                    "Log ingested",
                    "Trace correlated",
                    "Alert rule matched"
                ],
                null),
            new(
                "evt-telemetry-001",
                "2026-05-24 08:00 HST",
                "notice",
                "telemetry",
                "otlp",
                "OTLP exporter is not configured for production.",
                "prod",
                "honua-prod-01",
                "corr-telemetry-prod",
                "trace-telemetry-01",
                "req-telemetry-01",
                null,
                null,
                [
                    new("server status", "Telemetry status payload", "/operate/observability#telemetry", "Server reports metrics healthy, logs disabled, traces unknown, OTLP not configured.")
                ],
                [
                    new("environment", "Production", "/operate/observability#fleet")
                ],
                [
                    "Telemetry status fetched",
                    "Neutral states rendered without failing the environment"
                ],
                null),
            new(
                "evt-telemetry-004",
                "2026-05-24 08:00 HST",
                "notice",
                "telemetry",
                "admin realtime",
                "Admin realtime capability is unsupported in staging.",
                "staging",
                "honua-staging-02",
                "corr-telemetry-staging",
                "trace-telemetry-04",
                "req-telemetry-04",
                null,
                null,
                [
                    new("server status", "Capability payload", "/operate/observability#telemetry", "Server capability list omitted admin realtime and replica metrics.")
                ],
                [
                    new("environment", "Staging", "/operate/observability#fleet")
                ],
                [
                    "Capability status fetched",
                    "Unsupported capability rendered as neutral"
                ],
                null)
        ],
        Alerts:
        [
            new(
                "alert-slo-burn",
                "Publish SLO burn",
                "critical",
                new("firing", "Release gate SLO burn threshold is currently exceeded."),
                "release_gate",
                "Platform Ops",
                "2026-05-24 09:31 HST",
                "2026-05-24 09:34 HST",
                ["parcels-service", "generated-app-preview", "rel-2026.05.18"],
                [
                    new("event", "Alert event", "/operate/events/evt-alert-900", "Normalized alert event in the evidence timeline."),
                    new("grafana", "SLO burn panel", "https://grafana.example.invalid/d/slo-parcels", "External metrics panel.")
                ],
                new(
                    "The alert is tied to publish job job-publish-001 and should remain firing until preview failures are resolved.",
                    [
                        new("event", "Alert event", "/operate/events/evt-alert-900", "Primary event evidence."),
                        new("job", "Publish job", "/operate/jobs/job-publish-001", "Failed job detail.")
                    ],
                    [
                        "Acknowledge after assigning an owner.",
                        "Retry publish only after compatibility drift is resolved."
                    ])),
            new(
                "alert-sync-conflict",
                "Replica conflicts waiting",
                "warning",
                new("acknowledged", "On-call acknowledged the replica conflict alert."),
                "sync_conflict",
                "Field Ops",
                "2026-05-24 08:42 HST",
                "2026-05-24 09:10 HST",
                ["kona-field-team", "zoning-layer"],
                [
                    new("event", "Sync conflict event", "/operate/events/evt-sync-044", "Replica conflict event with maintenance job link.")
                ],
                new(
                    "The conflicts are contained to one replica and have a maintenance job attached.",
                    [
                        new("event", "Sync event", "/operate/events/evt-sync-044", "Primary sync evidence."),
                        new("job", "Maintenance job", "/operate/jobs/job-maint-001", "Conflict review job.")
                    ],
                    [
                        "Review the conflict report artifact.",
                        "Resolve feature edits before closing the alert."
                    ]))
        ],
        Rules:
        [
            new(
                "rule-release-slo",
                "Publish SLO burn",
                "release_slo",
                true,
                new("healthy", "Rule evaluated successfully in the last five minutes."),
                "Error budget burn > 2 percent for 30 minutes during release watch.",
                "ops-oncall email and Slack",
                "2026-05-24 09:32 HST",
                1,
                0,
                []),
            new(
                "rule-geofence-field-team",
                "Field crew leaves assigned zone",
                "geofence",
                false,
                new("invalid", "The rule cannot be enabled until it has a source layer and delivery channel."),
                "Trigger when crew asset exits the assigned work zone for more than 10 minutes.",
                "not configured",
                "never",
                0,
                0,
                ["Select a source layer.", "Configure at least one delivery channel."]),
            new(
                "rule-temporal-review",
                "Large temporal diff",
                "temporal_window",
                false,
                new("disabled", "Rule is valid but disabled by operator choice."),
                "Trigger when changed features exceed 10 percent of the layer in a 24 hour window.",
                "ops-oncall email",
                "2026-05-23 17:10 HST",
                0,
                0,
                []),
            new(
                "rule-data-quality",
                "Parcel geometry quality",
                "data_quality",
                true,
                new("degraded", "Delivery retry backlog is above the warning threshold."),
                "Trigger when invalid geometry count exceeds 25 after import.",
                "ops-oncall email, dead-letter retry",
                "2026-05-24 08:50 HST",
                0,
                3,
                [])
        ],
        Jobs:
        [
            Job(
                "job-studio-001",
                "Studio",
                "gp",
                "studio-analysis",
                new("succeeded", "Studio analysis package completed."),
                "mira@honua.example",
                "2026-05-24 07:12 HST",
                "staging",
                "honua-staging-02",
                100,
                "none",
                ["studio-project:watershed-dashboard", "content:watershed-score"],
                [
                    Stage("Queued", "succeeded", 100, "Studio submitted the package run."),
                    Stage("Analyze", "succeeded", 100, "Spatial analysis completed."),
                    Stage("Publish artifact", "succeeded", 100, "Result attached to Studio project.")
                ]),
            Job(
                "job-publish-001",
                "Publishing",
                "publish",
                "operator-publishing",
                new("failed", "Generated app preview stage failed."),
                "alex@honua.example",
                "2026-05-24 09:21 HST",
                "prod",
                "honua-prod-01",
                72,
                "package_binding_unsupported",
                ["content:parcels-service", "release:rel-2026.05.18", "app:parcel-dashboard"],
                [
                    Stage("Queued", "succeeded", 100, "Publish request accepted."),
                    Stage("Validate package", "warning", 100, "Package binding drift detected."),
                    Stage("Preview generated app", "failed", 34, "Dashboard binding unsupported by current preview runtime."),
                    Stage("Promote", "blocked", 0, "Promotion blocked until preview succeeds.")
                ]),
            Job(
                "job-gitops-001",
                "GitOps",
                "gitops_release",
                "gitops-release",
                new("waiting", "Release is waiting for operator compatibility review."),
                "release-bot",
                "2026-05-24 08:55 HST",
                "prod",
                "honua-prod-01",
                64,
                "compatibility_review_required",
                ["release:rel-2026.05.18", "git:refs/heads/release/prod"],
                [
                    Stage("Create proposal", "succeeded", 100, "Pull request merged."),
                    Stage("Compatibility check", "warning", 100, "Two bindings drifted."),
                    Stage("Deploy", "waiting", 0, "Waiting for operator review.")
                ]),
            Job(
                "job-temporal-001",
                "Temporal",
                "temporal_diff",
                "temporal",
                new("succeeded", "Temporal diff completed."),
                "sam@honua.example",
                "2026-05-24 08:19 HST",
                "staging",
                "honua-staging-02",
                100,
                "none",
                ["layer:zoning", "change-set:cs-zoning-20260524"],
                [
                    Stage("Select snapshots", "succeeded", 100, "Snapshots resolved."),
                    Stage("Compute diff", "succeeded", 100, "Feature diff completed."),
                    Stage("Attach report", "succeeded", 100, "Report is available.")
                ]),
            Job(
                "job-alert-001",
                "Alert delivery",
                "alert_delivery",
                "alerting",
                new("retrying", "Slack delivery is retrying after a provider timeout."),
                "alert-runner",
                "2026-05-24 09:31 HST",
                "prod",
                "honua-prod-01",
                45,
                "provider_timeout",
                ["alert:alert-slo-burn", "channel:ops-oncall"],
                [
                    Stage("Format message", "succeeded", 100, "Message payload rendered."),
                    Stage("Email delivery", "succeeded", 100, "Email accepted."),
                    Stage("Slack delivery", "retrying", 45, "Provider timeout; retry scheduled.")
                ]),
            Job(
                "job-import-001",
                "Import",
                "import",
                "data-import",
                new("running", "Import validation is running."),
                "lee@honua.example",
                "2026-05-24 09:05 HST",
                "staging",
                "honua-staging-02",
                58,
                "none",
                ["connection:county-sftp", "layer:building-permits"],
                [
                    Stage("Fetch", "succeeded", 100, "Source archive downloaded."),
                    Stage("Validate", "running", 58, "Geometry and schema validation in progress."),
                    Stage("Commit", "waiting", 0, "Waiting for validation.")
                ]),
            Job(
                "job-maint-001",
                "Maintenance",
                "maintenance",
                "maintenance",
                new("blocked", "Maintenance is blocked by unresolved sync conflicts."),
                "maintenance-runner",
                "2026-05-24 08:44 HST",
                "prod",
                "honua-prod-01",
                31,
                "sync_conflict_review_required",
                ["replica:kona-field-team", "layer:zoning"],
                [
                    Stage("Collect conflicts", "succeeded", 100, "Conflict report generated."),
                    Stage("Apply safe repairs", "blocked", 31, "Manual review required."),
                    Stage("Compact replica", "waiting", 0, "Waiting for repair stage.")
                ])
        ],
        Investigations:
        [
            new(
                "inv-prod-17",
                "Production publish SLO burn",
                new("mitigating", "Operator is reviewing package binding drift before retry."),
                "alex@honua.example",
                "2026-05-24 09:21-10:00 HST",
                ["evt-alert-900", "evt-job-301", "evt-log-410"],
                ["alert-slo-burn"],
                ["job-publish-001", "job-gitops-001"],
                [
                    "Pinned the alert, publish job, and raw preview renderer log.",
                    "Waiting for package owner to update unsupported dashboard binding."
                ]),
            new(
                "inv-sync-04",
                "Kona replica conflict review",
                new("open", "Field Ops is reviewing offline edits."),
                "field-ops@honua.example",
                "2026-05-24 08:42-09:30 HST",
                ["evt-sync-044"],
                ["alert-sync-conflict"],
                ["job-maint-001"],
                [
                    "Conflict report attached to maintenance job.",
                    "Do not compact replica until edits are resolved."
                ])
        ]);

    private static OperateJobStage Stage(string name, string status, int progressPercent, string detail) =>
        new(name, new(status, detail), progressPercent, detail);

    private static OperateJobRun Job(
        string jobRunId,
        string source,
        string jobType,
        string queue,
        OperateStatus status,
        string submittedBy,
        string submittedAt,
        string environmentId,
        string serverId,
        int progressPercent,
        string failureClassification,
        IReadOnlyList<string> resourceRefs,
        IReadOnlyList<OperateJobStage> stages)
    {
        var evidenceEventId = source switch
        {
            "Publishing" => "evt-job-301",
            "GitOps" => "evt-release-211",
            "Temporal" => "evt-data-071",
            "Alert delivery" => "evt-alert-900",
            "Maintenance" => "evt-sync-044",
            _ => "evt-telemetry-001"
        };

        return new(
            jobRunId,
            source,
            jobType,
            queue,
            status,
            submittedBy,
            submittedAt,
            environmentId,
            serverId,
            progressPercent,
            failureClassification,
            resourceRefs,
            stages,
            [
                $"{submittedAt} {jobRunId} accepted on queue {queue}",
                $"{submittedAt} {jobRunId} progress {progressPercent} percent",
                $"{submittedAt} {jobRunId} status {status.Label}"
            ],
            [
                new("report", "Execution report", $"{OperateObservabilityRoutes.JobDetail(jobRunId)}#artifacts", "Job runner report with stage timings and resource refs."),
                new("event", "Related event", OperateObservabilityRoutes.EventDetail(evidenceEventId), "Normalized event linked by correlation id.")
            ],
            [
                new("Progress", $"{progressPercent}%", status),
                new("Queue", queue, new("info", "Queue classification for operator filtering.")),
                new("Failure class", failureClassification, failureClassification == "none" ? new("healthy", "No failure classification.") : new("warning", "Failure classification requires operator review."))
            ],
            [
                new("Retry", string.Equals(status.Label, "failed", StringComparison.OrdinalIgnoreCase), "Retry is allowed only for failed jobs."),
                new("Cancel", status.Label is "running" or "retrying" or "waiting", "Cancel is allowed while work is active or waiting."),
                new("Promote", (source is "GitOps" or "Publishing") && status.Label is "waiting", "Promotion requires a waiting release or publish job.")
            ],
            [
                new("event", evidenceEventId, OperateObservabilityRoutes.EventDetail(evidenceEventId)),
                new("environment", environmentId, $"{OperateObservabilityRoutes.Observability}#fleet")
            ]);
    }
}
