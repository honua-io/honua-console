using System.Globalization;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Projects the server's Operate observability HTTP contracts into the existing
/// Console UI records (<see cref="OperateObservabilitySnapshot"/> and friends),
/// so the #41 Razor surface and components are unchanged in shape while binding
/// to live data. All state strings flow through <see cref="OperateStatus"/>: a
/// missing/disabled/unsupported telemetry signal stays neutral and never marks
/// an environment failed, and a server-declared <c>Allowed=false</c> is the only
/// thing that disables an action.
/// </summary>
public static class OperateObservabilityMapper
{
    public static OperateEnvironmentOverview MapEnvironment(
        ConsoleEnvironmentProfile profile,
        OperateStatus health,
        string driftSummary) =>
        new(
            EnvironmentId: profile.Id,
            Name: string.IsNullOrWhiteSpace(profile.DisplayName) ? profile.Id : profile.DisplayName,
            ServerName: profile.ServerBaseUri.Host,
            Version: "unknown",
            BuildSha: "unknown",
            Health: health,
            LastSeen: FormatTimestamp(profile.UpdatedAt),
            OwnerTeam: string.IsNullOrWhiteSpace(profile.TenantId) ? profile.EnvironmentKind : profile.TenantId,
            DriftSummary: driftSummary);

    // --- Events -------------------------------------------------------------

    public static IReadOnlyList<OperateEventRow> MapEvents(
        OperateEventPageResponse page,
        ConsoleEnvironmentProfile profile) =>
        page.Items.Select(item => MapEvent(item, profile)).ToArray();

    public static OperateEventRow MapEvent(OperateEventResponse item, ConsoleEnvironmentProfile profile)
    {
        var jobRunId = string.IsNullOrWhiteSpace(item.OperationId)
            ? ExtractRef(item.ResourceRef, "job")
            : item.OperationId;
        var alertId = ExtractRef(item.EventId, "alert") ?? ExtractRef(item.ResourceRef, "alert");

        return new OperateEventRow(
            EventId: item.EventId,
            EventTime: FormatTimestamp(item.OccurredAt),
            Severity: item.Severity,
            EventType: item.Kind,
            Category: item.ServiceId ?? item.Kind,
            Message: string.IsNullOrWhiteSpace(item.Title) ? (item.Summary ?? item.Kind) : item.Title,
            EnvironmentId: profile.Id,
            ServerId: profile.ServerBaseUri.Host,
            CorrelationId: item.CorrelationId ?? string.Empty,
            TraceId: item.TraceId ?? string.Empty,
            RequestId: item.RequestId ?? string.Empty,
            JobRunId: jobRunId,
            AlertId: alertId,
            RawEvidence: MapProviderLinks(item.ProviderLinks),
            RelatedObjects: MapEventRelated(item, jobRunId, alertId),
            Lifecycle: [],
            // #1168 does not project AI advisory; raw provider evidence is the
            // source of truth. Advisory remains null until the backlog item lands.
            AiAdvisory: null);
    }

    private static IReadOnlyList<OperateEvidenceLink> MapProviderLinks(
        IReadOnlyList<OperateProviderLinkResponse>? links) =>
        links is null
            ? []
            : links.Select(link => new OperateEvidenceLink(
                Kind: link.Provider,
                Label: link.Label,
                Href: link.Url,
                Detail: string.Empty)).ToArray();

    private static IReadOnlyList<OperateRelatedObject> MapEventRelated(
        OperateEventResponse item,
        string? jobRunId,
        string? alertId)
    {
        var related = new List<OperateRelatedObject>();
        if (!string.IsNullOrWhiteSpace(jobRunId))
        {
            related.Add(new("job", jobRunId, OperateObservabilityRoutes.JobDetail(jobRunId)));
        }

        if (!string.IsNullOrWhiteSpace(alertId))
        {
            related.Add(new("alert", alertId, OperateObservabilityRoutes.AlertDetail(alertId)));
        }

        AddRelated(related, "release", item.ReleaseId);
        AddRelated(related, "replica", item.ReplicaId);
        AddRelated(related, "change set", item.ChangeSetId);
        AddRelated(related, "service", item.ServiceId);
        return related;
    }

    private static void AddRelated(ICollection<OperateRelatedObject> related, string kind, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            related.Add(new(kind, value, OperateObservabilityRoutes.Observability));
        }
    }

    // --- Logs ---------------------------------------------------------------

    public static OperateLogsView MapLogs(OperateLogPageResponse page)
    {
        var logs = page.Items.Select(MapLog).ToArray();
        var severityBuckets = logs
            .GroupBy(item => item.Level, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .Select(group => new OperateLogGroup(
                Titleize(group.Key),
                group.Count(),
                new OperateStatus(group.Key, $"{group.Count().ToString(CultureInfo.InvariantCulture)} matching log entries.")))
            .ToArray();

        var exceptionGroups = logs
            .GroupBy(item => NormalizeLogGroup(item.Message), StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .Take(8)
            .Select(group => new OperateLogGroup(
                group.Key,
                group.Count(),
                new OperateStatus(group.Any(item => item.StatusCode >= 500) ? "error" : "warning", "Grouped by sanitized log message.")))
            .ToArray();

        return new OperateLogsView(page.InstanceId, page.Capacity, logs, severityBuckets, exceptionGroups);
    }

    private static OperateLogRecord MapLog(OperateLogEntryResponse entry)
    {
        var level = string.IsNullOrWhiteSpace(entry.Level) ? "warning" : entry.Level;
        return new OperateLogRecord(
            Timestamp: FormatTimestamp(entry.Timestamp),
            Level: level,
            Severity: new OperateStatus(level, entry.Message),
            Path: entry.Path ?? string.Empty,
            StatusCode: entry.StatusCode,
            CorrelationId: entry.CorrelationId,
            Message: entry.Message,
            ProviderLinks:
            [
                new OperateEvidenceLink(
                    "server log",
                    string.IsNullOrWhiteSpace(entry.CorrelationId) ? "Recent error buffer" : entry.CorrelationId,
                    OperateObservabilityRoutes.Observability + "#logs",
                    "Structured log entry returned by /api/v1/admin/observability/logs.")
            ]);
    }

    private static string NormalizeLogGroup(string message)
    {
        var normalized = string.IsNullOrWhiteSpace(message) ? "No message" : message.Trim();
        var newlineIndex = normalized.IndexOf('\n', StringComparison.Ordinal);
        if (newlineIndex >= 0)
        {
            normalized = normalized[..newlineIndex];
        }

        const int maxLength = 96;
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }

    // --- Alerts -------------------------------------------------------------

    public static IReadOnlyList<OperateAlertRecord> MapAlerts(ObservabilityAlertEventPageResponse page) =>
        page.Items.Select(MapAlert).ToArray();

    public static OperateAlertRecord MapAlert(ObservabilityAlertEventResponse item)
    {
        var lifecycle = NormalizeToken(item.LifecycleStatus);
        var alertId = item.EventId.ToString(CultureInfo.InvariantCulture);
        var eventRef = $"alert:{item.EventId.ToString(CultureInfo.InvariantCulture)}";

        return new OperateAlertRecord(
            AlertId: alertId,
            Title: string.IsNullOrWhiteSpace(item.RuleName)
                ? $"{Titleize(item.TriggerType)} alert {alertId}"
                : item.RuleName!,
            Severity: item.Severity,
            Status: new OperateStatus(
                item.LifecycleStatus,
                $"{Titleize(item.TriggerType)} trigger, incident {NormalizeToken(item.IncidentStatus)}."),
            Source: $"realtime:{NormalizeToken(item.TriggerType)}",
            OwnerTeam: item.ServiceId,
            StartedAt: FormatTimestamp(item.OccurredAt),
            LastSeenAt: FormatTimestamp(item.OccurredAt.AddMilliseconds(item.IncidentDurationMs)),
            AffectedResources: BuildAlertResources(item),
            // Alerts are a typed projection over the event feed; the immutable
            // event detail is always linked as raw evidence.
            EvidenceLinks:
            [
                new OperateEvidenceLink(
                    "event",
                    $"Alert event {alertId}",
                    OperateObservabilityRoutes.EventDetail(eventRef),
                    "Immutable alert event in the evidence timeline.")
            ],
            AiAdvisory: null,
            Actions: BuildAlertActions(lifecycle));
    }

    private static IReadOnlyList<string> BuildAlertResources(ObservabilityAlertEventResponse item)
    {
        var resources = new List<string> { item.ResourceRef };
        if (!string.IsNullOrWhiteSpace(item.ServiceId))
        {
            resources.Add($"service:{item.ServiceId}");
        }

        resources.Add($"layer:{item.LayerId.ToString(CultureInfo.InvariantCulture)}");
        if (item.ZoneId is { } zoneId)
        {
            resources.Add($"zone:{zoneId.ToString(CultureInfo.InvariantCulture)}");
        }

        return resources;
    }

    private static IReadOnlyList<OperateAlertAction> BuildAlertActions(string lifecycle)
    {
        var resolved = string.Equals(lifecycle, "resolved", StringComparison.Ordinal);
        var acknowledged = string.Equals(lifecycle, "acknowledged", StringComparison.Ordinal);
        return
        [
            new OperateAlertAction(
                "Acknowledge",
                !resolved && !acknowledged,
                resolved ? "Alert is resolved." : acknowledged ? "Already acknowledged." : OperateActionPresentation.ActionExecutionDeferredReason),
            new OperateAlertAction(
                "Suppress",
                !resolved,
                resolved ? "Alert is resolved." : OperateActionPresentation.ActionExecutionDeferredReason),
            new OperateAlertAction(
                "Resolve",
                !resolved,
                resolved ? "Already resolved." : OperateActionPresentation.ActionExecutionDeferredReason)
        ];
    }

    // --- Rules + zones ------------------------------------------------------

    public static OperateAlertRule MapRule(AlertRuleResponse rule, AlertRuleHealthResponse? health)
    {
        var validation = BuildRuleValidation(rule, health);
        var deliveryFailures = health?.DeliveryFailureCount ?? 0;
        var channelFailing = health?.DeliveryChannels.Any(channel => OperateStatusIsFailure(channel.Status)) ?? false;
        var status = ResolveRuleStatus(rule, deliveryFailures, channelFailing, validation.Count > 0);

        return new OperateAlertRule(
            RuleId: rule.RuleId.ToString(CultureInfo.InvariantCulture),
            Name: rule.RuleName,
            RuleType: rule.ZoneId is null ? NormalizeToken(rule.TriggerType) : $"geofence:{NormalizeToken(rule.TriggerType)}",
            Enabled: rule.IsActive,
            Status: status,
            ConditionSummary: $"{Titleize(rule.TriggerType)} trigger, cooldown {rule.CooldownSeconds.ToString(CultureInfo.InvariantCulture)}s, severity {rule.Severity}.",
            DeliverySummary: rule.Channels.Length == 0 ? "no channel configured" : string.Join(", ", rule.Channels),
            LastEvaluatedAt: FormatTimestamp(health?.LastEvaluatedAt),
            ActiveIncidentCount: health?.ActiveIncidentCount ?? 0,
            DeliveryFailureCount: deliveryFailures,
            ValidationMessages: validation,
            RecentTriggerCount: health?.RecentTriggerCount ?? 0,
            LastTriggeredAt: FormatTimestamp(health?.LastTriggeredAt));
    }

    private static IReadOnlyList<string> BuildRuleValidation(AlertRuleResponse rule, AlertRuleHealthResponse? health)
    {
        var messages = new List<string>();
        if (rule.Channels.Length == 0)
        {
            messages.Add("Configure at least one delivery channel.");
        }

        if (health is not null)
        {
            foreach (var channel in health.DeliveryChannels.Where(channel => OperateStatusIsFailure(channel.Status) || string.Equals(channel.Status, "unconfigured", StringComparison.OrdinalIgnoreCase)))
            {
                var detail = string.IsNullOrWhiteSpace(channel.LastError) ? channel.Status : channel.LastError;
                messages.Add($"Channel {channel.Channel}: {detail}");
            }
        }

        return messages;
    }

    private static OperateStatus ResolveRuleStatus(
        AlertRuleResponse rule,
        int deliveryFailures,
        bool channelFailing,
        bool hasValidationErrors)
    {
        if (hasValidationErrors)
        {
            return new("invalid", "Rule has validation errors and cannot be enabled.");
        }

        if (channelFailing)
        {
            return new("failing", "One or more delivery channels are failing.");
        }

        if (deliveryFailures > 0)
        {
            return new("degraded", "Delivery retries are above the warning threshold.");
        }

        return rule.IsActive
            ? new("healthy", "Rule is enabled and delivery channels are healthy.")
            : new("disabled", "Rule is valid but disabled by operator choice.");
    }

    public static OperateGeofenceZone MapZone(AlertZoneResponse zone) =>
        new(
            ZoneId: zone.ZoneId.ToString(CultureInfo.InvariantCulture),
            Name: zone.ZoneName,
            ServiceId: zone.ServiceId,
            Active: zone.IsActive,
            Srid: zone.Srid ?? 0,
            // Geometry is echoed from server metadata only; Console performs no
            // client-side geodesy for this surface.
            GeometrySummary: string.IsNullOrWhiteSpace(zone.Wkt)
                ? "no geometry on file"
                : $"WKT geometry ({zone.Wkt!.Length.ToString(CultureInfo.InvariantCulture)} chars)",
            Metadata: zone.Metadata
                .Select(pair => $"{pair.Key}: {pair.Value}")
                .ToArray());

    // --- Jobs ---------------------------------------------------------------

    public static OperateJobRun MapJobSummary(ConsoleJobSummary summary, ConsoleEnvironmentProfile profile) =>
        new(
            JobRunId: summary.JobId,
            Source: summary.Kind,
            JobType: string.IsNullOrWhiteSpace(summary.Backend) ? summary.TargetKind : summary.Backend,
            Queue: summary.Queue ?? string.Empty,
            Status: new OperateStatus(summary.Status, summary.LatestEvent?.Message ?? summary.WorkloadName),
            SubmittedBy: summary.RequestedBy ?? "unknown",
            SubmittedAt: FormatTimestamp(summary.CreatedAt),
            EnvironmentId: profile.Id,
            ServerId: profile.ServerBaseUri.Host,
            ProgressPercent: ToPercent(summary.PercentComplete),
            FailureClassification: string.Equals(summary.Status, "Failed", StringComparison.OrdinalIgnoreCase) ? "see job detail" : "none",
            ResourceRefs: summary.ResourceRefs,
            Stages: [],
            Logs: [],
            Artifacts: [],
            Metrics: BuildSummaryMetrics(summary),
            AllowedActions: [],
            RelatedObjects: BuildJobRelated(summary));

    public static OperateJobRun MapJobDetail(
        ConsoleJobDetail detail,
        IReadOnlyList<string> logs,
        IReadOnlyList<OperateEvidenceLink> artifacts,
        ConsoleEnvironmentProfile profile) =>
        new(
            JobRunId: detail.JobId,
            Source: detail.Kind,
            JobType: string.IsNullOrWhiteSpace(detail.Backend) ? detail.TargetKind : detail.Backend,
            Queue: detail.Queue ?? string.Empty,
            Status: new OperateStatus(detail.Status, detail.LatestEvent?.Message ?? detail.WorkloadName),
            SubmittedBy: detail.RequestedBy ?? "unknown",
            SubmittedAt: FormatTimestamp(detail.CreatedAt),
            EnvironmentId: profile.Id,
            ServerId: profile.ServerBaseUri.Host,
            ProgressPercent: ToPercent(detail.PercentComplete),
            FailureClassification: detail.Failure?.Classification ?? "none",
            ResourceRefs: detail.ResourceRefs,
            Stages: detail.Stages.Select(MapStage).ToArray(),
            Logs: logs,
            Artifacts: artifacts,
            Metrics: BuildDetailMetrics(detail),
            AllowedActions: detail.Actions.Select(MapJobAction).ToArray(),
            RelatedObjects: BuildJobRelated(detail));

    public static OperateJobAction MapJobAction(ConsoleJobActionDescriptor action) =>
        new(
            Label: Titleize(action.Name),
            IsAllowed: action.Allowed,
            Reason: action.Allowed
                ? OperateActionPresentation.ActionExecutionDeferredReason
                : string.IsNullOrWhiteSpace(action.DisabledReason) ? "Not permitted by the server." : action.DisabledReason!);

    public static IReadOnlyList<string> MapJobLogs(ConsoleJobLogPageResponse page)
    {
        if (!string.Equals(page.State, "available", StringComparison.OrdinalIgnoreCase))
        {
            return ["Job logs are not available from the provider for this job."];
        }

        return page.Items
            .Select(entry => $"{FormatTimestamp(entry.Timestamp)} [{entry.Level}] {entry.Message}")
            .ToArray();
    }

    public static IReadOnlyList<OperateEvidenceLink> MapJobArtifacts(ConsoleJobArtifactPageResponse page) =>
        page.Items.Select(artifact => new OperateEvidenceLink(
            Kind: artifact.Kind ?? "artifact",
            Label: artifact.Label ?? artifact.ArtifactId,
            Href: artifact.ProviderLink ?? string.Empty,
            Detail: string.IsNullOrWhiteSpace(artifact.Message)
                ? $"{artifact.Availability} artifact"
                : artifact.Message!)).ToArray();

    private static OperateJobStage MapStage(ConsoleJobStage stage)
    {
        var timing = (stage.StartedAt, stage.CompletedAt) switch
        {
            (null, _) => "not started",
            ({ } started, null) => $"started {FormatTimestamp(started)}",
            ({ } started, { } completed) => $"{FormatTimestamp(started)} - {FormatTimestamp(completed)}"
        };

        return new OperateJobStage(
            Name: stage.Name,
            Status: new OperateStatus(stage.Status, timing),
            ProgressPercent: ToPercent(stage.PercentComplete),
            Detail: timing);
    }

    private static IReadOnlyList<OperateJobMetric> BuildSummaryMetrics(ConsoleJobSummary summary) =>
    [
        new("Progress", $"{ToPercent(summary.PercentComplete).ToString(CultureInfo.InvariantCulture)}%", new OperateStatus(summary.Status, "Job status")),
        new("Attempts", $"{summary.AttemptCount.ToString(CultureInfo.InvariantCulture)}/{summary.MaxAttempts.ToString(CultureInfo.InvariantCulture)}", new OperateStatus("info", "Attempt count")),
        new("Artifacts", summary.ArtifactCount.ToString(CultureInfo.InvariantCulture), new OperateStatus("info", "Artifacts produced"))
    ];

    private static IReadOnlyList<OperateJobMetric> BuildDetailMetrics(ConsoleJobDetail detail) =>
    [
        new("Progress", $"{ToPercent(detail.PercentComplete).ToString(CultureInfo.InvariantCulture)}%", new OperateStatus(detail.Status, "Job status")),
        new("Attempts", $"{detail.AttemptCount.ToString(CultureInfo.InvariantCulture)}/{detail.MaxAttempts.ToString(CultureInfo.InvariantCulture)}", new OperateStatus("info", "Attempt count")),
        new("Duration", detail.DurationMs is { } ms ? $"{ms.ToString(CultureInfo.InvariantCulture)} ms" : "n/a", new OperateStatus("info", "Wall-clock duration")),
        new("Failure class", detail.Failure?.Classification ?? "none", detail.Failure is null ? new OperateStatus("healthy", "No failure classification.") : new OperateStatus("warning", "Failure classification requires review."))
    ];

    private static IReadOnlyList<OperateRelatedObject> BuildJobRelated(ConsoleJobSummary summary)
    {
        var related = new List<OperateRelatedObject>();
        if (summary.Links is { EventsByJob: { } byJob } && !string.IsNullOrWhiteSpace(byJob))
        {
            related.Add(new("events", "Events for this job", byJob));
        }

        if (summary.Links is { EventsByCorrelation: { } byCorrelation } && !string.IsNullOrWhiteSpace(byCorrelation))
        {
            related.Add(new("events", "Correlated events", byCorrelation));
        }

        if (!string.IsNullOrWhiteSpace(summary.CorrelationId))
        {
            related.Add(new("correlation", summary.CorrelationId!, OperateObservabilityRoutes.Observability));
        }

        return related;
    }

    // --- Investigations -----------------------------------------------------

    public static IReadOnlyList<OperateInvestigation> MapInvestigations(InvestigationPageResponse page) =>
        page.Items.Select(MapInvestigation).ToArray();

    public static OperateInvestigation MapInvestigation(InvestigationSummaryResponse item) =>
        new(
            InvestigationId: item.InvestigationId,
            Title: item.Title,
            Status: new OperateStatus(item.Status, item.Summary ?? "Investigation"),
            Owner: item.CreatedBy,
            TimeRange: $"{FormatTimestamp(item.CreatedAt)} - {FormatTimestamp(item.UpdatedAt)}",
            PinnedEventIds: [],
            LinkedAlertIds: [],
            LinkedJobRunIds: [],
            Notes: BuildInvestigationNotes(item));

    public static OperateInvestigation MapInvestigation(InvestigationResponse item)
    {
        var linkedAlerts = item.Links
            .Where(link => string.Equals(link.ResourceKind, "alert", StringComparison.OrdinalIgnoreCase))
            .Select(link => link.ResourceId)
            .ToArray();
        var linkedJobs = item.Links
            .Where(link => string.Equals(link.ResourceKind, "job", StringComparison.OrdinalIgnoreCase))
            .Select(link => link.ResourceId)
            .ToArray();
        var extraLinks = item.Links
            .Where(link => !string.Equals(link.ResourceKind, "alert", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(link.ResourceKind, "job", StringComparison.OrdinalIgnoreCase))
            .Select(link => $"{link.ResourceKind}: {link.ResourceId}")
            .ToArray();

        var notes = new List<string>();
        if (!string.IsNullOrWhiteSpace(item.Summary))
        {
            notes.Add(item.Summary!);
        }

        notes.AddRange(item.Pins.Where(pin => !string.IsNullOrWhiteSpace(pin.Note)).Select(pin => pin.Note!));
        notes.AddRange(item.Links.Where(link => !string.IsNullOrWhiteSpace(link.Note)).Select(link => link.Note!));
        notes.AddRange(extraLinks);
        if (notes.Count == 0)
        {
            notes.Add("No operator notes recorded.");
        }

        return new OperateInvestigation(
            InvestigationId: item.InvestigationId,
            Title: item.Title,
            Status: new OperateStatus(item.Status, item.Summary ?? "Investigation"),
            Owner: item.CreatedBy,
            TimeRange: $"{FormatTimestamp(item.CreatedAt)} - {FormatTimestamp(item.UpdatedAt)}",
            PinnedEventIds: item.Pins.Select(pin => pin.EventRef).ToArray(),
            LinkedAlertIds: linkedAlerts,
            LinkedJobRunIds: linkedJobs,
            Notes: notes);
    }

    private static IReadOnlyList<string> BuildInvestigationNotes(InvestigationSummaryResponse item)
    {
        var notes = new List<string>();
        if (!string.IsNullOrWhiteSpace(item.Summary))
        {
            notes.Add(item.Summary!);
        }

        notes.Add($"{item.PinCount.ToString(CultureInfo.InvariantCulture)} pinned events");
        notes.Add($"{item.LinkCount.ToString(CultureInfo.InvariantCulture)} linked resources");
        return notes;
    }

    // --- Helpers ------------------------------------------------------------

    internal static string FormatTimestamp(DateTimeOffset value) =>
        value.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);

    internal static string FormatTimestamp(DateTimeOffset? value) =>
        value is { } resolved ? FormatTimestamp(resolved) : "never";

    private static int ToPercent(double? value) =>
        value is { } resolved ? Math.Clamp((int)Math.Round(resolved), 0, 100) : 0;

    private static string? ExtractRef(string? value, string kind)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (value.StartsWith(kind + ":", StringComparison.OrdinalIgnoreCase))
        {
            return value[(kind.Length + 1)..];
        }

        if (value.StartsWith(kind + "/", StringComparison.OrdinalIgnoreCase))
        {
            return value[(kind.Length + 1)..];
        }

        return null;
    }

    private static bool OperateStatusIsFailure(string status) =>
        NormalizeToken(status) is "failing" or "unauthorized" or "rate limited" or "disabled";

    private static string NormalizeToken(string value) =>
        value.Trim().ToLowerInvariant().Replace("_", " ", StringComparison.Ordinal);

    private static string Titleize(string value)
    {
        var normalized = NormalizeToken(value);
        return normalized.Length == 0
            ? normalized
            : char.ToUpperInvariant(normalized[0]) + normalized[1..];
    }
}
