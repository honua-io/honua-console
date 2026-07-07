namespace Honua.Console.Shell.Models;

/// <summary>
/// One row for the shared <c>OperateTimeline</c> component (console#293): an operate event's
/// kind, severity, correlation id, timestamp, message, and drill-in link, normalized away from
/// any one domain's wire/view-model shape (<see cref="OperateEventRow"/> today; deploy-operation
/// and health transition events in follow-on tickets) so one component can render all of them.
///
/// <see cref="DedupeKey"/> is the idempotency key the timeline dedupes on: primarily the
/// server-issued event id, falling back to <c>operationId:transitionKind</c> for a transition
/// event that has no event id of its own. The server's transition seam is at-least-once
/// (honua-server PR #2577 review note) — the same event can arrive twice (a duplicate push, or a
/// poll re-reading an event already delivered live) — so dedup lives here, once, rather than
/// being re-implemented by every consumer.
/// </summary>
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
    /// <summary>The shared status-vocabulary projection of <see cref="Severity"/> (console#293).</summary>
    public OperateStatus SeverityStatus => new(Severity, Message);

    /// <summary>
    /// The idempotency key the timeline dedupes on: the event id when present, else the
    /// operation id + transition kind pair. When neither identifies the event, a stable key is
    /// derived from the row's content so two genuinely distinct, unidentified rows are not
    /// collapsed into one.
    /// </summary>
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

/// <summary>
/// Pure dedup helper for <see cref="OperateTimelineEntry"/> (console#293). Given a source
/// sequence that may contain the same event more than once (at-least-once delivery), returns the
/// entries in their original relative order with only the first occurrence of each
/// <see cref="OperateTimelineEntry.DedupeKey"/> kept — an idempotent append, computed once here
/// rather than by each timeline consumer.
/// </summary>
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

    /// <summary>
    /// Idempotently appends <paramref name="incoming"/> to <paramref name="existing"/>: a no-op
    /// when an entry with the same <see cref="OperateTimelineEntry.DedupeKey"/> is already
    /// present, otherwise the incoming entry is added to the end. This is the primitive a
    /// realtime consumer (the admin hub pushes transition events one at a time) calls per event,
    /// so the at-least-once delivery guarantee never produces a duplicate row.
    /// </summary>
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

/// <summary>Projects the Operate observability event row onto the shared timeline entry shape.</summary>
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
