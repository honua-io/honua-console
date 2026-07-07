namespace Honua.Console.Shell.Services;

/// <summary>
/// The honest connection state for a console realtime hub client (console#293 shared
/// realtime/capability seam, extracted from the approval inbox's
/// <c>SignalRConsoleProposalRealtimeClient</c> pattern, honua-server #1695).
///
/// This is the fix for finding PA-233: previously a connect/subscribe/reconnect failure was
/// swallowed by a bare <c>catch {}</c> with no logger, so a live surface could silently degrade
/// to its fallback (manual refresh / poll) with no visible signal. <see cref="FallbackEngaged"/>
/// distinguishes "we tried and it failed" from <see cref="NotConfigured"/> ("nothing to try —
/// no environment is bound"), so a Live/Manual pill can render the true reason rather than a
/// single collapsed boolean.
/// </summary>
public enum ConsoleRealtimeConnectionState
{
    /// <summary>
    /// No environment profile is bound, so there is nothing to connect to. This is an expected,
    /// inert state — not a failure — and is never logged as one.
    /// </summary>
    NotConfigured,

    /// <summary>A connection/reconnection attempt is in flight.</summary>
    Connecting,

    /// <summary>The hub connection is live and the client is joined to its group.</summary>
    Connected,

    /// <summary>
    /// A connect, subscribe, or reconnect attempt failed, or a live connection closed and could
    /// not resume. The client has degraded to its fallback (manual refresh and/or poll) and the
    /// engagement has been logged with its cause — never silent.
    /// </summary>
    FallbackEngaged,
}
