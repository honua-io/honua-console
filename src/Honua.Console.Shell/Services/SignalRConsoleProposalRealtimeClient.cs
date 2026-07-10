using Honua.Console.Shell.Models;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Live <see cref="IConsoleProposalRealtimeClient"/> backed by the honua-server admin
/// realtime hub (honua-server #1695). It connects from the console's server process to
/// <c>{server}/hubs/admin</c> via <see cref="ConsoleAdminHubConnectionFactory"/>, joins the
/// <c>proposals</c> group via the hub's <c>SubscribeToProposals</c> method, and projects each
/// <c>ProposalPending</c> / <c>ProposalResolved</c> payload onto a <see cref="ConsoleProposalEvent"/>.
///
/// This is also the reference implementation of the console#293 shared realtime/capability seam
/// (<see cref="IConsoleRealtimeCapabilityClient"/>): connection lifecycle and auth are delegated
/// to <see cref="ConsoleAdminHubConnectionFactory"/>, and fallback engagement is tracked through
/// <see cref="ConsoleRealtimeFallbackTracker"/> rather than the previous bare <c>catch {}</c>
/// blocks (finding PA-233) — every connect/subscribe/reconnect failure and every unexpected close
/// is now logged and reflected in <see cref="ConnectionState"/>, so the inbox's Live/Manual pill
/// can be honest about whether it silently degraded. A missing environment binding still leaves
/// the client inert (<see cref="ConsoleRealtimeConnectionState.NotConfigured"/>) and never
/// fabricates events; the inbox stays usable via manual refresh either way.
/// </summary>
public sealed class SignalRConsoleProposalRealtimeClient : IConsoleProposalRealtimeClient, IConsoleRealtimeCapabilityClient
{
    internal const string HubPath = "hubs/admin";
    internal const string SubscribeMethod = "SubscribeToProposals";
    internal const string UnsubscribeMethod = "UnsubscribeFromProposals";
    internal const string PendingEvent = "ProposalPending";
    internal const string ResolvedEvent = "ProposalResolved";

    private readonly IConsoleEnvironmentProfileStore _profileStore;
    private readonly IConsoleAccountSessionStore _sessions;
    private readonly ILogger _logger;
    private readonly string? _adminApiKey;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ConsoleRealtimeFallbackTracker _tracker;

    private HubConnection? _connection;
    private bool _stopping;

    public SignalRConsoleProposalRealtimeClient(
        IConsoleEnvironmentProfileStore profileStore,
        IConsoleAccountSessionStore sessions,
        ILogger<SignalRConsoleProposalRealtimeClient> logger,
        string? adminApiKey = null)
    {
        _profileStore = profileStore ?? throw new ArgumentNullException(nameof(profileStore));
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _adminApiKey = string.IsNullOrWhiteSpace(adminApiKey) ? null : adminApiKey;
        _tracker = new ConsoleRealtimeFallbackTracker(_logger, HubPath);
    }

    /// <inheritdoc />
    public event Action<ConsoleProposalEvent>? ProposalChanged;

    /// <inheritdoc cref="IConsoleRealtimeCapabilityClient.ConnectionStateChanged" />
    public event Action<ConsoleRealtimeConnectionState>? ConnectionStateChanged
    {
        add => _tracker.StateChanged += value;
        remove => _tracker.StateChanged -= value;
    }

    /// <inheritdoc cref="IConsoleRealtimeCapabilityClient.ConnectionState" />
    public ConsoleRealtimeConnectionState ConnectionState => _tracker.State;

    /// <inheritdoc />
    public bool IsConnected => ConnectionState == ConsoleRealtimeConnectionState.Connected;

    /// <inheritdoc />
    public bool IsFallbackEngaged => ConnectionState == ConsoleRealtimeConnectionState.FallbackEngaged;

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_connection is not null)
            {
                return;
            }

            HubConnection? connection;
            try
            {
                _tracker.MarkConnecting();
                connection = await ConsoleAdminHubConnectionFactory
                    .CreateAsync(_profileStore, _sessions, _adminApiKey, HubPath, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // PA-233 fix: previously unguarded (a profile-store/bearer-resolution failure
                // would bubble past this method entirely). Logged and surfaced as a fallback.
                _tracker.MarkFallbackEngaged("Failed to resolve the active environment or build the admin hub connection.", ex);
                return;
            }

            if (connection is null)
            {
                // No environment bound: stay inert. The inbox renders the missing-binding
                // state from its REST read; there is nothing to subscribe to. Not a failure.
                _tracker.MarkNotConfigured();
                return;
            }

            connection.On<ProposalRealtimeWire>(
                PendingEvent,
                wire => Raise(ConsoleProposalEventKind.Pending, wire));
            connection.On<ProposalRealtimeWire>(
                ResolvedEvent,
                wire => Raise(ConsoleProposalEventKind.Resolved, wire));

            connection.Reconnecting += _ =>
            {
                _tracker.MarkConnecting();
                return Task.CompletedTask;
            };

            // Re-join the proposals group after an automatic reconnect, else the connection
            // would be live but no longer in the group and would silently miss events.
            connection.Reconnected += async _ =>
            {
                try
                {
                    await connection.InvokeAsync(SubscribeMethod).ConfigureAwait(false);
                    _tracker.MarkConnected();
                }
                catch (Exception ex)
                {
                    // PA-233 fix: this was a bare `catch {}` — silent. The connection is live but
                    // not in the group, so events would be missed with no signal that it happened.
                    _tracker.MarkFallbackEngaged("Reconnected but re-subscribing to the proposals group failed.", ex);
                }
            };

            connection.Closed += ex =>
            {
                // An unexpected close (server restart, or automatic reconnect exhausted) used to
                // leave IsConnected=false with no record of why. StopAsync sets _stopping first,
                // so a deliberate shutdown is not misreported as a fallback.
                if (!_stopping)
                {
                    _tracker.MarkFallbackEngaged("The hub connection closed.", ex);
                }

                return Task.CompletedTask;
            };

            try
            {
                await connection.StartAsync(cancellationToken).ConfigureAwait(false);
                await connection.InvokeAsync(SubscribeMethod, cancellationToken).ConfigureAwait(false);
                _connection = connection;
                _tracker.MarkConnected();
            }
            catch (Exception ex)
            {
                // PA-233 fix: this was a bare `catch {}` — a connect/subscribe failure left the
                // client disconnected and silent, with no logger and no way for the page to know
                // the difference between "not configured" and "tried and failed".
                _tracker.MarkFallbackEngaged("Failed to connect or subscribe to the proposals group.", ex);
                await connection.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        _stopping = true;
        try
        {
            if (_connection is null)
            {
                _tracker.MarkNotConfigured();
                return;
            }

            var connection = _connection;
            _connection = null;

            try
            {
                if (connection.State == HubConnectionState.Connected)
                {
                    await connection.InvokeAsync(UnsubscribeMethod, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                // Best-effort unsubscribe; disposing the connection drops the group membership
                // anyway. Logged at Debug (expected during shutdown/reconnect races), not a fallback.
                _logger.LogDebug(ex, "Best-effort unsubscribe from {HubPath} failed before disposing the connection.", HubPath);
            }

            await connection.DisposeAsync().ConfigureAwait(false);
            _tracker.MarkNotConfigured();
        }
        finally
        {
            _stopping = false;
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _gate.Dispose();
    }

    private void Raise(ConsoleProposalEventKind kind, ProposalRealtimeWire wire) =>
        ProposalChanged?.Invoke(MapEvent(kind, wire));

    /// <summary>Projects a hub wire payload onto a <see cref="ConsoleProposalEvent"/>.</summary>
    internal static ConsoleProposalEvent MapEvent(ConsoleProposalEventKind kind, ProposalRealtimeWire wire) => new(
        EventKind: kind,
        ProposalId: wire.ProposalId ?? string.Empty,
        Kind: ConsoleProposalPresentation.MapKind(wire.Kind),
        Status: ConsoleProposalPresentation.MapStatus(wire.Status),
        RequestedBy: wire.RequestedBy,
        RiskLevel: ConsoleProposalPresentation.MapRisk(wire.RiskLevel),
        GeneratedAt: wire.GeneratedAt);
}

/// <summary>
/// Wire shape of the admin hub's proposal event payload (honua-server
/// <c>ProposalRealtimeEvent</c>, #1695). Bound case-insensitively from the hub's camelCase
/// JSON protocol.
/// </summary>
public sealed record ProposalRealtimeWire
{
    public string? ProposalId { get; init; }
    public string? Kind { get; init; }
    public string? Status { get; init; }
    public string? RequestedBy { get; init; }
    public string? RiskLevel { get; init; }
    public DateTimeOffset GeneratedAt { get; init; }
}
