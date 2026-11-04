namespace Honua.Console.Shell.Models;

public enum CorrelationIdKind
{
    CorrelationId,

    OperationId,

    EventId,

    FindingId,

    ProposalId,
}

public static class CorrelationIdRoutes
{
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
