namespace Honua.Console.Shell.Models;

/// <summary>
/// The identifier kinds the shared <c>CorrelationIdChip</c> component (console#292) knows how
/// to route: a correlation id shared across an alert/metric/finding/proposal chain, a governed
/// deploy-control operation id, a normalized observability event id, a deterministic ops-finding
/// id, or an operation-gateway proposal id. Each kind resolves onto exactly one existing Console
/// surface (<see cref="CorrelationIdRoutes"/>) — the golden path from a fired alert to its
/// finding, proposal, and inbox approval never requires hand-copying an id between pages.
/// </summary>
public enum CorrelationIdKind
{
    /// <summary>A correlation id shared by every event/log/alert in one causal chain.</summary>
    CorrelationId,

    /// <summary>A governed deploy-control operation id (server upgrade or metadata promotion).</summary>
    OperationId,

    /// <summary>A normalized Operate observability event id.</summary>
    EventId,

    /// <summary>A deterministic ops-finding id (Copilot Findings).</summary>
    FindingId,

    /// <summary>An operation-gateway proposal id (the Approval inbox's work item).</summary>
    ProposalId,
}

/// <summary>
/// Resolves a <see cref="CorrelationIdKind"/> + raw id onto the Console route that owns it, so
/// the correlation-id chip never needs a per-caller href (console#292). Every route here already
/// exists on trunk (or is added alongside this ticket as a client-side query-string convenience);
/// no new server endpoint is introduced (issue #292 non-goals).
/// </summary>
public static class CorrelationIdRoutes
{
    /// <summary>Resolves the deep-link href for an id of the given kind.</summary>
    public static string Resolve(CorrelationIdKind kind, string value)
    {
        var trimmed = value.Trim();
        return kind switch
        {
            CorrelationIdKind.EventId => OperateObservabilityRoutes.EventDetail(trimmed),
            CorrelationIdKind.CorrelationId => OperateObservabilityRoutes.CorrelationSearch(trimmed),
            CorrelationIdKind.FindingId => OperateObservabilityRoutes.FindingDetail(trimmed),
            CorrelationIdKind.ProposalId => OperateObservabilityRoutes.ProposalDetail(trimmed),
            CorrelationIdKind.OperationId => OperateObservabilityRoutes.OperationDetail(trimmed),
            _ => OperateObservabilityRoutes.Root,
        };
    }

    /// <summary>A short human label for the kind, used in the chip's accessible name/title.</summary>
    public static string KindLabel(CorrelationIdKind kind) => kind switch
    {
        CorrelationIdKind.EventId => "event",
        CorrelationIdKind.CorrelationId => "correlated events",
        CorrelationIdKind.FindingId => "finding",
        CorrelationIdKind.ProposalId => "proposal",
        CorrelationIdKind.OperationId => "deploy operation",
        _ => "record",
    };
}
