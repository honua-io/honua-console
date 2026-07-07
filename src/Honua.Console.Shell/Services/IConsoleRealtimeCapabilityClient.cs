namespace Honua.Console.Shell.Services;

/// <summary>
/// The narrow, role-scoped contract every admin-hub-backed realtime client exposes (console#293
/// shared seam, PA-242 first slice: this lands as its own small interface rather than widening
/// <c>IHonuaAdminOperateClient</c>). A client that groups-subscribes to a honua-server admin hub
/// (proposals today; deploy operations / health in follow-on tickets) implements this alongside
/// its domain-specific event contract (e.g. <see cref="IConsoleProposalRealtimeClient"/>) so any
/// consumer can render an honest Live/Manual/fallback pill without knowing the domain events.
///
/// <see cref="ConnectionState"/> is the PA-233 fix: it distinguishes "no environment bound"
/// (<see cref="ConsoleRealtimeConnectionState.NotConfigured"/>) from "we tried and it failed"
/// (<see cref="ConsoleRealtimeConnectionState.FallbackEngaged"/>) instead of collapsing both into
/// a single <c>IsConnected == false</c>.
/// </summary>
public interface IConsoleRealtimeCapabilityClient : IAsyncDisposable
{
    /// <summary>The current connection state.</summary>
    ConsoleRealtimeConnectionState ConnectionState { get; }

    /// <summary>Raised whenever <see cref="ConnectionState"/> changes.</summary>
    event Action<ConsoleRealtimeConnectionState>? ConnectionStateChanged;

    /// <summary>Whether a live hub connection is currently established and joined to its group.</summary>
    bool IsConnected => ConnectionState == ConsoleRealtimeConnectionState.Connected;

    /// <summary>
    /// Whether the client has degraded to its fallback after a connect/subscribe/reconnect
    /// failure (as opposed to never having anything to connect to). A page's Live/Manual pill
    /// should treat this distinctly from the inert "no environment bound" state.
    /// </summary>
    bool IsFallbackEngaged => ConnectionState == ConsoleRealtimeConnectionState.FallbackEngaged;

    /// <summary>
    /// Establishes the hub connection for the active environment and joins its group. Idempotent
    /// and best-effort: a failure to connect moves <see cref="ConnectionState"/> to
    /// <see cref="ConsoleRealtimeConnectionState.FallbackEngaged"/> (logged) rather than throwing.
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>Leaves the group and tears the connection down.</summary>
    Task StopAsync(CancellationToken cancellationToken = default);
}
