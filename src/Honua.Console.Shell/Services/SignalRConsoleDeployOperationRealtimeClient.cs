using Honua.Console.Shell.Models;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Live <see cref="IConsoleDeployOperationRealtimeClient"/> backed by the honua-server admin
/// realtime hub's <c>deploy-operations</c> group (honua-server#2554). Mirrors
/// <see cref="SignalRConsoleProposalRealtimeClient"/> exactly (same hub, same
/// connect/reconnect/dispose pattern via <see cref="ConsoleAdminHubConnectionFactory"/> and
/// <see cref="ConsoleRealtimeFallbackTracker"/>) — the console#293 shared seam this ticket
/// consumes rather than reinventing.
///
/// honua-server#2554 is still open at the time this client was authored, so joining the group
/// fails against every server available today; that failure is caught exactly like any other
/// subscribe failure and reported as <see cref="ConsoleRealtimeConnectionState.FallbackEngaged"/>
/// — the deploy cockpit's poll loop (the approval panel's existing <see cref="PeriodicTimer"/>)
/// stays the source of truth until the group exists. This is the "poll fallback until the hub
/// group exists" behavior console#290's acceptance criteria require, expressed honestly through
/// the same connection-state vocabulary the inbox's Live/Manual pill already uses.
/// </summary>
public sealed class SignalRConsoleDeployOperationRealtimeClient : IConsoleDeployOperationRealtimeClient, IConsoleRealtimeCapabilityClient
{
    internal const string HubPath = "hubs/admin";
    internal const string SubscribeMethod = "SubscribeToDeployOperations";
    internal const string UnsubscribeMethod = "UnsubscribeFromDeployOperations";
    internal const string TransitionEvent = "DeployOperationTransition";

    private readonly IConsoleEnvironmentProfileStore _profileStore;
    private readonly IConsoleAccountSessionStore _sessions;
    private readonly ILogger _logger;
    private readonly string? _adminApiKey;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ConsoleRealtimeFallbackTracker _tracker;

    private HubConnection? _connection;
    private bool _stopping;

    public SignalRConsoleDeployOperationRealtimeClient(
        IConsoleEnvironmentProfileStore profileStore,
        IConsoleAccountSessionStore sessions,
        ILogger<SignalRConsoleDeployOperationRealtimeClient> logger,
        string? adminApiKey = null)
    {
        _profileStore = profileStore ?? throw new ArgumentNullException(nameof(profileStore));
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _adminApiKey = string.IsNullOrWhiteSpace(adminApiKey) ? null : adminApiKey;
        _tracker = new ConsoleRealtimeFallbackTracker(_logger, HubPath + "/" + SubscribeMethod);
    }

    /// <inheritdoc />
    public event Action<ConsoleDeployOperationRealtimeEvent>? OperationChanged;

    /// <inheritdoc cref="IConsoleRealtimeCapabilityClient.ConnectionStateChanged" />
    public event Action<ConsoleRealtimeConnectionState>? ConnectionStateChanged
    {
        add => _tracker.StateChanged += value;
        remove => _tracker.StateChanged -= value;
    }

    /// <inheritdoc cref="IConsoleRealtimeCapabilityClient.ConnectionState" />
    public ConsoleRealtimeConnectionState ConnectionState => _tracker.State;

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
                _tracker.MarkFallbackEngaged("Failed to resolve the active environment or build the admin hub connection.", ex);
                return;
            }

            if (connection is null)
            {
                // No environment bound: stay inert, exactly like the proposals realtime client.
                _tracker.MarkNotConfigured();
                return;
            }

            connection.On<DeployOperationTransitionWire>(TransitionEvent, wire => Raise(wire));

            connection.Reconnecting += _ =>
            {
                _tracker.MarkConnecting();
                return Task.CompletedTask;
            };

            connection.Reconnected += async _ =>
            {
                try
                {
                    await connection.InvokeAsync(SubscribeMethod).ConfigureAwait(false);
                    _tracker.MarkConnected();
                }
                catch (Exception ex)
                {
                    _tracker.MarkFallbackEngaged("Reconnected but re-subscribing to the deploy-operations group failed.", ex);
                }
            };

            connection.Closed += ex =>
            {
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
                // Expected against every server today (honua-server#2554 not yet merged): the hub
                // method does not exist, InvokeAsync throws, and the cockpit falls back to polling
                // — logged at the tracker's normal level, not an error condition on our side.
                _tracker.MarkFallbackEngaged("Failed to connect or subscribe to the deploy-operations group.", ex);
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

    private void Raise(DeployOperationTransitionWire wire) => OperationChanged?.Invoke(new ConsoleDeployOperationRealtimeEvent(
        wire.OperationId ?? string.Empty,
        wire.TransitionKind ?? string.Empty,
        wire.Status ?? string.Empty,
        wire.CorrelationId,
        wire.GeneratedAt));
}

/// <summary>
/// Wire shape of the admin hub's deploy-operation transition payload (honua-server#2554,
/// projecting the honua-server PR #2577 <c>WorkflowOperationTransition</c> seam). Bound
/// case-insensitively from the hub's camelCase JSON protocol.
/// </summary>
public sealed record DeployOperationTransitionWire
{
    public string? OperationId { get; init; }
    public string? TransitionKind { get; init; }
    public string? Status { get; init; }
    public string? CorrelationId { get; init; }
    public DateTimeOffset GeneratedAt { get; init; }
}
