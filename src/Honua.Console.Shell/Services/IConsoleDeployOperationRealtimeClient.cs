using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// One deploy-operation transition pushed by the honua-server admin realtime hub's
/// <c>deploy-operations</c> group (honua-server#2554 — the workflow-transition seam
/// honua-server PR #2577 introduced feeding a second consumer). Projected onto a shared
/// <see cref="OperateTimelineEntry"/> via <see cref="ConsoleDeployOperationRealtimeEventExtensions.ToTimelineEntry"/>
/// so it dedupes with any poll-sourced rows on <c>operationId:transitionKind</c> (the
/// server's transition seam is at-least-once).
/// </summary>
public sealed record ConsoleDeployOperationRealtimeEvent(
    string OperationId,
    string TransitionKind,
    string Status,
    string? CorrelationId,
    DateTimeOffset GeneratedAt);

/// <summary>
/// Subscribes the deploy cockpit to the honua-server admin realtime hub's
/// <c>deploy-operations</c> group (honua-server#2554) so canary-step / health-gate /
/// promote / rollback transitions update the operations list and the upgrade card live,
/// without polling. Console#290 acceptance criterion 2: honua-server#2554 is not yet merged
/// at the time this seam was authored, so every connected server today fails the group-join
/// call and the implementation degrades to <see cref="ConsoleRealtimeConnectionState.FallbackEngaged"/>
/// — callers MUST keep their existing poll loop running (e.g.
/// <c>OperateDeploymentApprovalPanel</c>'s <see cref="System.Threading.PeriodicTimer"/>) rather
/// than assume this seam alone keeps them live; that is the "poll fallback until the hub group
/// exists" behavior console#293's shared seam was designed to make honest.
/// </summary>
public interface IConsoleDeployOperationRealtimeClient : IAsyncDisposable
{
    /// <summary>Raised on the connection's thread for each deploy-operation transition received.</summary>
    event Action<ConsoleDeployOperationRealtimeEvent>? OperationChanged;

    /// <summary>
    /// Establishes the hub connection for the active environment and joins the
    /// <c>deploy-operations</c> group. Idempotent and best-effort: a failure (including "the
    /// connected server does not yet expose this group") moves the shared
    /// <see cref="IConsoleRealtimeCapabilityClient.ConnectionState"/> to
    /// <see cref="ConsoleRealtimeConnectionState.FallbackEngaged"/> rather than throwing.
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>Leaves the group and tears the connection down.</summary>
    Task StopAsync(CancellationToken cancellationToken = default);
}

/// <summary>Projects the realtime deploy-operation transition onto the shared timeline entry shape.</summary>
public static class ConsoleDeployOperationRealtimeEventExtensions
{
    public static OperateTimelineEntry ToTimelineEntry(this ConsoleDeployOperationRealtimeEvent evt) => new(
        Kind: "deploy-operation",
        Severity: evt.Status,
        Message: $"{evt.OperationId}: {evt.TransitionKind} -> {evt.Status}",
        Timestamp: evt.GeneratedAt.ToUniversalTime().ToString("u"),
        CorrelationId: evt.CorrelationId ?? string.Empty,
        OperationId: evt.OperationId,
        TransitionKind: evt.TransitionKind,
        DetailHref: CorrelationIdRoutes.Resolve(CorrelationIdKind.OperationId, evt.OperationId));
}
