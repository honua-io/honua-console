namespace Honua.Console.Shell.Models;

public sealed record OperateTimelineEntry(
    string Kind,
    string Severity,
    string Message,
    string Timestamp,
    string CorrelationId,
    string? EventId = null,
    string? OperationId = null,
    string? TransitionKind = null,
    string? DetailHref = null)
{
    public OperateStatus SeverityStatus => new(Severity, Message);

    public string DedupeKey
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(EventId))
            {
                return $"event:{EventId.Trim()}";
            }

            if (!string.IsNullOrWhiteSpace(OperationId) || !string.IsNullOrWhiteSpace(TransitionKind))
            {
                return $"transition:{(OperationId ?? string.Empty).Trim()}:{(TransitionKind ?? string.Empty).Trim()}";
            }

            return $"content:{Kind}:{Timestamp}:{CorrelationId}:{Message}";
        }
    }
}

public static class OperateTimelineEntries
{
    public static IReadOnlyList<OperateTimelineEntry> Deduplicate(IEnumerable<OperateTimelineEntry> source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<OperateTimelineEntry>();

        foreach (var entry in source)
        {
            if (seen.Add(entry.DedupeKey))
            {
                result.Add(entry);
            }
        }

        return result;
    }

    public static IReadOnlyList<OperateTimelineEntry> Append(
        IReadOnlyList<OperateTimelineEntry> existing,
        OperateTimelineEntry incoming)
    {
        ArgumentNullException.ThrowIfNull(existing);

        foreach (var entry in existing)
        {
            if (string.Equals(entry.DedupeKey, incoming.DedupeKey, StringComparison.Ordinal))
            {
                return existing;
            }
        }

        var result = new List<OperateTimelineEntry>(existing.Count + 1);
        result.AddRange(existing);
        result.Add(incoming);
        return result;
    }
}

public static class OperateTimelineProjections
{
    public static OperateTimelineEntry ToTimelineEntry(this OperateEventRow row) => new(
        Kind: row.EventType,
        Severity: row.Severity,
        Message: row.Message,
        Timestamp: row.EventTime,
        CorrelationId: row.CorrelationId,
        EventId: row.EventId,
        DetailHref: row.DetailHref);
}
