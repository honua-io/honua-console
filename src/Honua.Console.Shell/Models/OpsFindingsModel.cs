using System.Globalization;
using Honua.Console.Contracts;

namespace Honua.Console.Shell.Models;

public sealed record OpsFindingView(
    string Id,
    string Rule,
    OperateStatus Severity,
    string SeverityLabel,
    string Title,
    string Explanation,
    string DetectedAt,
    DateTimeOffset DetectedAtValue,
    IReadOnlyList<OpsFindingSubjectRow> Subject,
    IReadOnlyList<string> EvidenceRefs,
    OpsFindingActionView? RecommendedAction,
    string? OperationId = null)
{
    public bool HasAction => RecommendedAction is not null;

    public string InvestigateHref => string.IsNullOrWhiteSpace(OperationId)
        ? $"{OperateObservabilityRoutes.Observability}#events"
        : CorrelationIdRoutes.Resolve(CorrelationIdKind.OperationId, OperationId);
}

public sealed record OpsFindingSubjectRow(string Label, string Value);

public sealed record OpsFindingActionView(
    string Kind,
    string Summary,
    string Reason,
    bool AutoSafe,
    int BlastRadius);

public static class OpsFindingsMapper
{
    public static IReadOnlyList<OpsFindingView> Map(OpsFindingsListResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return (response.Findings ?? [])
            .Select(MapFinding)
            .ToArray();
    }

    private static OpsFindingView MapFinding(OpsFindingResponse finding)
    {
        var severity = SeverityStatus(finding.Severity);
        return new OpsFindingView(
            finding.Id ?? string.Empty,
            string.IsNullOrWhiteSpace(finding.Rule) ? "(unknown rule)" : finding.Rule!,
            severity,
            string.IsNullOrWhiteSpace(finding.Severity) ? severity.Label : finding.Severity!,
            string.IsNullOrWhiteSpace(finding.Title) ? "(untitled finding)" : finding.Title!,
            finding.Explanation ?? string.Empty,
            FormatTimestamp(finding.DetectedAt),
            finding.DetectedAt,
            MapSubject(finding.Subject),
            finding.EvidenceRefs ?? [],
            finding.RecommendedAction is null
                ? null
                : new OpsFindingActionView(
                    string.IsNullOrWhiteSpace(finding.RecommendedAction.Kind) ? "action" : finding.RecommendedAction.Kind!,
                    finding.RecommendedAction.Summary ?? string.Empty,
                    finding.RecommendedAction.Reason ?? string.Empty,
                    finding.RecommendedAction.AutoSafe,
                    Math.Max(1, finding.RecommendedAction.BlastRadius)),
            finding.Subject?.OperationId);
    }

    private static IReadOnlyList<OpsFindingSubjectRow> MapSubject(OpsFindingSubjectResponse? subject)
    {
        if (subject is null)
        {
            return [];
        }

        var rows = new List<OpsFindingSubjectRow>(6);
        AddRow(rows, "Target", subject.TargetId);
        AddRow(rows, "Workload", subject.WorkloadId);
        AddRow(rows, "Channel", subject.Channel);
        AddRow(rows, "Operation", subject.OperationId);
        AddRow(rows, "Release", subject.ReleaseVersion);
        AddRow(rows, "Protocol", subject.Protocol);
        return rows;
    }

    private static void AddRow(List<OpsFindingSubjectRow> rows, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            rows.Add(new OpsFindingSubjectRow(label, value!));
        }
    }

    // Severity maps to the shared status vocabulary so the chip colors match the rest of
    // Operate: Critical -> danger, Warning -> warning, Info -> info/neutral.
    private static OperateStatus SeverityStatus(string? severity) =>
        (severity?.Trim().ToLowerInvariant()) switch
        {
            "critical" => new OperateStatus("critical", "Critical: an active operational problem requiring prompt action."),
            "warning" => new OperateStatus("warning", "Warning: a condition that may degrade service if unaddressed."),
            "info" => new OperateStatus("info", "Informational: an observation with no urgency."),
            null or "" => new OperateStatus("unknown", "Severity not reported."),
            _ => new OperateStatus("info", severity!)
        };

    private static string FormatTimestamp(DateTimeOffset timestamp) =>
        timestamp == default
            ? "unknown"
            : timestamp.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);
}
