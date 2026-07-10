using System.Globalization;
using System.Text.Json;
using Honua.Console.Contracts;

namespace Honua.Console.Shell.Models;

/// <summary>
/// A server-confirmed autonomy snapshot: global settings, durable per-rule policy and
/// track records, plus the policy/action audit events projected by the unified Operate feed.
/// </summary>
public sealed record OpsAutonomySnapshot(
    OpsAutonomySettingsResponse Settings,
    IReadOnlyList<OpsAutonomyPolicyResponse> Policies,
    IReadOnlyList<OpsAutonomyAuditEntry> AuditEntries,
    bool AuditPartialResult = false,
    string AuditMessage = "");

/// <summary>A domain label paired with the shared Operate status vocabulary.</summary>
public sealed record OpsAutonomyOutcome(string Label, OperateStatus Status);

/// <summary>
/// One autonomy-related audit event. Causal fields are parsed only from server-projected
/// evidence; a missing <c>detailsJson</c> stays explicitly unreported rather than inferred.
/// </summary>
public sealed record OpsAutonomyAuditEntry(
    string EventId,
    DateTimeOffset OccurredAt,
    string Action,
    string Actor,
    string CorrelationId,
    string ResourceRef,
    string? FindingId,
    string? Rule,
    IReadOnlyList<string> EvidenceRefs,
    string OutcomeEvidence,
    OpsAutonomyOutcome Outcome,
    bool IsPolicyChange,
    string? OperationId = null,
    string? OperationKind = null,
    string? ActionDiscriminator = null,
    string? PolicyMode = null,
    bool? KillSwitchEnabled = null)
{
    /// <summary>Projects this event through the shared, at-least-once-safe timeline primitive.</summary>
    public OperateTimelineEntry ToTimelineEntry() => new(
        Kind: IsPolicyChange ? "policy" : "autonomy",
        Severity: Outcome.Status.State,
        Message: $"{Outcome.Label}: {OutcomeEvidence}",
        Timestamp: OccurredAt == default
            ? "unknown"
            : OccurredAt.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture),
        CorrelationId: CorrelationId,
        EventId: EventId,
        OperationId: IsPolicyChange
            ? null
            : string.IsNullOrWhiteSpace(OperationId) ? CorrelationId : OperationId,
        TransitionKind: Action,
        DetailHref: string.IsNullOrWhiteSpace(EventId)
            ? null
            : OperateObservabilityRoutes.EventDetail(EventId));
}

/// <summary>Maps unified Operate audit events into the autonomy causal-audit shape.</summary>
public static class OpsAutonomyAuditMapper
{
    private const string MissingEvidence = "Evidence detail was not projected by this server event.";
    private const int MaxDetailsJsonLength = 64 * 1024;
    private const int MaxEvidenceRefs = 12;
    private const int MaxDetailValueLength = 256;

    /// <summary>Maps and newest-first sorts autonomy action and policy events.</summary>
    public static IReadOnlyList<OpsAutonomyAuditEntry> Map(IEnumerable<OperateEventResponse> events)
    {
        ArgumentNullException.ThrowIfNull(events);

        return events
            .Where(IsAutonomyEvent)
            .Select(MapEvent)
            .GroupBy(
                item => string.IsNullOrWhiteSpace(item.EventId)
                    ? $"{item.ResourceRef}:{item.Action}:{item.OccurredAt:O}"
                    : item.EventId,
                StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderByDescending(item => item.OccurredAt)
            .ThenByDescending(item => item.EventId, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool IsAutonomyEvent(OperateEventResponse item) =>
        item.ResourceRef?.StartsWith("operation_autonomy/", StringComparison.Ordinal) == true
        || item.ResourceRef?.StartsWith("ops_autonomy_policy/", StringComparison.Ordinal) == true
        || item.Title.StartsWith("operation.auto_", StringComparison.Ordinal)
        || item.Title.StartsWith("ops_autonomy.", StringComparison.Ordinal);

    private static OpsAutonomyAuditEntry MapEvent(OperateEventResponse item)
    {
        var details = ParseDetails(item.DetailsJson);
        var resourceRef = item.ResourceRef ?? string.Empty;
        var policyChange = resourceRef.StartsWith("ops_autonomy_policy/", StringComparison.Ordinal)
            || item.Title.StartsWith("ops_autonomy.", StringComparison.Ordinal);
        var resourceId = ExtractResourceId(resourceRef);
        var findingId = FirstNonBlank(details.FindingId, policyChange ? null : resourceId);
        var rule = FirstNonBlank(details.Rule, policyChange && resourceId != "global" ? resourceId : null);

        return new OpsAutonomyAuditEntry(
            EventId: item.EventId,
            OccurredAt: item.OccurredAt,
            Action: item.Title,
            Actor: item.Actor ?? string.Empty,
            CorrelationId: item.CorrelationId ?? string.Empty,
            ResourceRef: resourceRef,
            FindingId: findingId,
            Rule: rule,
            EvidenceRefs: details.EvidenceRefs,
            OutcomeEvidence: BuildOutcomeEvidence(details),
            Outcome: MapOutcome(item.Title, item.Severity),
            IsPolicyChange: policyChange,
            OperationId: details.OperationId,
            OperationKind: details.Kind,
            ActionDiscriminator: details.ActionDiscriminator,
            PolicyMode: details.Mode,
            KillSwitchEnabled: details.KillSwitchEnabled);
    }

    private static OpsAutonomyOutcome MapOutcome(string action, string severity) => action switch
    {
        "operation.auto_executed" => Outcome("Executed", "info", action),
        "operation.auto_verified" => StageOutcome(
            severity,
            successLabel: "Verified",
            failureLabel: "Verification failed",
            action),
        "operation.auto_compensated" => StageOutcome(
            severity,
            successLabel: "Compensated",
            failureLabel: "Compensation failed",
            action,
            successState: "rolled back"),
        "operation.auto_applied" => Outcome("Auto-applied", "succeeded", action),
        "operation.auto_failed" => Outcome("Failed", "failed", action),
        "operation.auto_rolled_back" => Outcome("Rolled back", "rolled back", action),
        "operation.auto_indeterminate" => Outcome("Manual intervention required", "manual intervention required", action),
        "operation.auto_canceled" => Outcome("Canceled", "unknown", action),
        "ops_autonomy.policy.update" => Outcome("Policy changed", "info", action),
        "ops_autonomy.settings.update" => Outcome("Global policy changed", "info", action),
        _ => Outcome("Autonomy event", string.IsNullOrWhiteSpace(severity) ? "unknown" : severity, action)
    };

    private static OpsAutonomyOutcome Outcome(string label, string state, string action) =>
        new(label, new OperateStatus(state, $"Server audit action {action}."));

    private static OpsAutonomyOutcome StageOutcome(
        string severity,
        string successLabel,
        string failureLabel,
        string action,
        string successState = "succeeded")
    {
        var normalized = severity.Trim().ToLowerInvariant();
        return normalized switch
        {
            "error" or "critical" or "failed" => Outcome(failureLabel, "failed", action),
            "warning" => Outcome($"{successLabel} with warning", "warning", action),
            _ => Outcome(successLabel, successState, action)
        };
    }

    private static string? ExtractResourceId(string resourceRef)
    {
        var separator = resourceRef.IndexOf('/');
        return separator >= 0 && separator < resourceRef.Length - 1
            ? resourceRef[(separator + 1)..]
            : null;
    }

    private static ParsedDetails ParseDetails(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > MaxDetailsJsonLength)
        {
            return ParsedDetails.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return ParsedDetails.Empty;
            }

            var root = document.RootElement;
            var evidence = new List<string>();
            var uniqueEvidence = new HashSet<string>(StringComparer.Ordinal);
            if (root.TryGetProperty("evidenceRefs", out var refs)
                && refs.ValueKind == JsonValueKind.Array)
            {
                foreach (var value in refs.EnumerateArray())
                {
                    if (value.ValueKind == JsonValueKind.String
                        && !string.IsNullOrWhiteSpace(value.GetString()))
                    {
                        var normalized = Bound(value.GetString()!);
                        if (uniqueEvidence.Add(normalized))
                        {
                            evidence.Add(normalized);
                            if (evidence.Count == MaxEvidenceRefs)
                            {
                                break;
                            }
                        }
                    }
                }
            }

            return new ParsedDetails(
                ReadString(root, "findingId"),
                ReadString(root, "rule"),
                ReadString(root, "operationId"),
                ReadString(root, "status"),
                ReadString(root, "message"),
                ReadString(root, "kind"),
                ReadString(root, "actionDiscriminator"),
                ReadString(root, "mode"),
                ReadBoolean(root, "killSwitchEnabled"),
                evidence);
        }
        catch (JsonException)
        {
            return ParsedDetails.Empty;
        }
    }

    private static string? ReadString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? Bound(value.GetString() ?? string.Empty)
            : null;

    private static string Bound(string value)
    {
        var normalized = value.Trim();
        return normalized.Length <= MaxDetailValueLength
            ? normalized
            : normalized[..MaxDetailValueLength];
    }

    private static bool? ReadBoolean(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value)
            && value.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? value.GetBoolean()
                : null;

    private static string BuildOutcomeEvidence(ParsedDetails details)
    {
        var parts = new List<string>(4);
        AddPart(parts, "Status", details.Status);
        AddPart(parts, "Evidence", details.Message);
        AddPart(parts, "Operation", details.OperationId);
        AddPart(parts, "Mode", details.Mode);
        if (details.KillSwitchEnabled is { } enabled)
        {
            parts.Add($"Kill switch: {(enabled ? "enabled" : "disabled")}");
        }

        if (parts.Count == 0)
        {
            return MissingEvidence;
        }

        var evidence = string.Join("; ", parts);
        return evidence.EndsWith('.') ? evidence : evidence + ".";
    }

    private static void AddPart(ICollection<string> parts, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            parts.Add($"{label}: {value}");
        }
    }

    private static string? FirstNonBlank(string? first, string? second) =>
        !string.IsNullOrWhiteSpace(first) ? first : !string.IsNullOrWhiteSpace(second) ? second : null;

    private sealed record ParsedDetails(
        string? FindingId,
        string? Rule,
        string? OperationId,
        string? Status,
        string? Message,
        string? Kind,
        string? ActionDiscriminator,
        string? Mode,
        bool? KillSwitchEnabled,
        IReadOnlyList<string> EvidenceRefs)
    {
        public static ParsedDetails Empty { get; } = new(null, null, null, null, null, null, null, null, null, []);
    }
}
