using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Subscribes the approval inbox to the honua-server admin realtime hub's proposals group
/// (honua-server #1695) so <c>ProposalPending</c> / <c>ProposalResolved</c> events update the
/// inbox and timeline live, without polling. The console connects from its server process to
/// the honua-server SignalR hub at <c>/hubs/admin</c>, joins the <c>proposals</c> group, and
/// raises <see cref="ProposalChanged"/> for each event it receives.
///
/// The connection binds to the active environment profile; when no environment is bound (or
/// the host cannot open a live connection) the implementation is an inert no-op and the inbox
/// degrades to an explicit manual refresh rather than fabricating events.
/// </summary>
public interface IConsoleProposalRealtimeClient : IAsyncDisposable
{
    /// <summary>Raised on the connection's thread for each proposal event received.</summary>
    event Action<ConsoleProposalEvent>? ProposalChanged;

    /// <summary>Whether a live hub connection is currently established.</summary>
    bool IsConnected { get; }

    /// <summary>
    /// Establishes the hub connection for the active environment and joins the proposals
    /// group. Idempotent and best-effort: a failure to connect leaves the client in the
    /// disconnected state (the inbox stays usable via manual refresh) and does not throw.
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>Leaves the proposals group and tears the connection down.</summary>
    Task StopAsync(CancellationToken cancellationToken = default);
}
