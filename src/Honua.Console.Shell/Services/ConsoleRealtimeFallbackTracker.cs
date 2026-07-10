using Microsoft.Extensions.Logging;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Tracks a realtime hub client's <see cref="ConsoleRealtimeConnectionState"/> and logs every
/// transition into <see cref="ConsoleRealtimeConnectionState.FallbackEngaged"/> with its cause
/// (console#293 shared seam; fixes finding PA-233). This is the one place a connect/subscribe/
/// reconnect failure is allowed to be swallowed into a fallback state — and it is never silent:
/// every engagement is logged, and <see cref="StateChanged"/> lets a page (or a shared client
/// wrapper) surface the honest state on its Live/Manual pill instead of a single collapsed
/// "connected" boolean that cannot distinguish "never tried" from "tried and failed".
/// </summary>
public sealed class ConsoleRealtimeFallbackTracker
{
    private readonly ILogger _logger;
    private readonly string _hubPath;

    public ConsoleRealtimeFallbackTracker(ILogger logger, string hubPath)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _hubPath = string.IsNullOrWhiteSpace(hubPath) ? "(unknown hub)" : hubPath;
    }

    /// <summary>The current connection state. Starts <see cref="ConsoleRealtimeConnectionState.NotConfigured"/>.</summary>
    public ConsoleRealtimeConnectionState State { get; private set; } = ConsoleRealtimeConnectionState.NotConfigured;

    /// <summary>Raised whenever <see cref="State"/> changes.</summary>
    public event Action<ConsoleRealtimeConnectionState>? StateChanged;

    /// <summary>No environment is bound; there is nothing to connect to. Not a failure, not logged.</summary>
    public void MarkNotConfigured() => SetState(ConsoleRealtimeConnectionState.NotConfigured);

    /// <summary>A connection/reconnection attempt is starting.</summary>
    public void MarkConnecting() => SetState(ConsoleRealtimeConnectionState.Connecting);

    /// <summary>The hub connection is live and joined to its group.</summary>
    public void MarkConnected() => SetState(ConsoleRealtimeConnectionState.Connected);

    /// <summary>
    /// A connect, subscribe, or reconnect attempt failed (or a live connection closed
    /// unexpectedly). Logs the cause and transitions to <see cref="ConsoleRealtimeConnectionState.FallbackEngaged"/>.
    /// </summary>
    /// <param name="reason">A short, human-readable description of what failed.</param>
    /// <param name="exception">The exception that caused the fallback, when there is one.</param>
    public void MarkFallbackEngaged(string reason, Exception? exception = null)
    {
        // PA-233 fix: this used to be a bare `catch {}` at each of several call sites in
        // SignalRConsoleProposalRealtimeClient. Every fallback engagement is now logged with its
        // hub path and cause, and observable via State/StateChanged.
        _logger.LogWarning(exception, "Realtime hub {HubPath} degraded to fallback: {Reason}", _hubPath, reason);
        SetState(ConsoleRealtimeConnectionState.FallbackEngaged);
    }

    private void SetState(ConsoleRealtimeConnectionState next)
    {
        if (State == next)
        {
            return;
        }

        State = next;
        StateChanged?.Invoke(State);
    }
}
