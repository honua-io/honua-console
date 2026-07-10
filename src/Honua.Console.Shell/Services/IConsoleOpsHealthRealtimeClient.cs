using Honua.Console.Contracts;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Subscribes the Ops Health page's trend charts to the honua-server admin realtime hub's
/// <c>ops-health</c> group (honua-server PR #2591 — console#288's addendum, 2026-07-06: SignalR,
/// not SSE). Every push carries the SAME cluster-aggregated snapshot DTO as
/// <c>GET .../ops-health</c> (<see cref="OpsHealthSnapshotResponse"/>), on the sampler-flush
/// cadence (~30-60s) plus immediately on an overall-status change.
///
/// Mirrors <see cref="IConsoleDeployOperationRealtimeClient"/> exactly — the console#293 shared
/// seam this ticket consumes rather than reinventing. honua-server PR #2591 was still landing at
/// the time this client was authored (console#288's dispatch note: "merges within the hour"), so
/// against a server that predates it the group-join fails and the seam degrades to
/// <see cref="ConsoleRealtimeConnectionState.FallbackEngaged"/> — the trend charts' history-refresh
/// poll stays the source of truth until the group exists, exactly like the deploy-operations and
/// proposals clients before it. This is also the honest capability-detection behavior the
/// addendum requires: the group is only advertised when the server has a Redis backplane, so a
/// no-Redis deployment degrades to the same fallback, not a false "Live" pill.
/// </summary>
public interface IConsoleOpsHealthRealtimeClient : IAsyncDisposable
{
    /// <summary>Raised on the connection's thread for each pushed ops-health snapshot.</summary>
    event Action<OpsHealthSnapshotResponse>? SnapshotReceived;

    /// <summary>
    /// Establishes the hub connection for the active environment and joins the <c>ops-health</c>
    /// group. Idempotent and best-effort: a failure (including "the connected server does not yet
    /// expose this group") moves the shared
    /// <see cref="IConsoleRealtimeCapabilityClient.ConnectionState"/> to
    /// <see cref="ConsoleRealtimeConnectionState.FallbackEngaged"/> rather than throwing.
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>Leaves the group and tears the connection down.</summary>
    Task StopAsync(CancellationToken cancellationToken = default);
}
