using Honua.Console.Contracts;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Live <see cref="IConsoleOpsHealthRealtimeClient"/> backed by the honua-server admin realtime
/// hub's <c>ops-health</c> group (honua-server PR #2591). Mirrors
/// <see cref="SignalRConsoleDeployOperationRealtimeClient"/> exactly (same hub, same
/// connect/reconnect/dispose pattern via <see cref="ConsoleAdminHubConnectionFactory"/> and
/// <see cref="ConsoleRealtimeFallbackTracker"/>) — the console#293 shared seam this ticket
/// consumes rather than reinventing.
///
/// The pushed payload is bound directly as <see cref="OpsHealthSnapshotResponse"/> (no
/// intermediate wire record): the addendum pins this as the SAME DTO the REST snapshot read
/// returns, so there is exactly one shape to keep in sync with the server, not two.
/// </summary>
public sealed class SignalRConsoleOpsHealthRealtimeClient : IConsoleOpsHealthRealtimeClient, IConsoleRealtimeCapabilityClient
{
    internal const string HubPath = "hubs/admin";
    internal const string SubscribeMethod = "SubscribeToOpsHealth";
    internal const string UnsubscribeMethod = "UnsubscribeFromOpsHealth";
    internal const string SnapshotEvent = "OpsHealthUpdated";

    private readonly IConsoleEnvironmentProfileStore _profileStore;
    private readonly IConsoleAccountSessionStore _sessions;
    private readonly ILogger _logger;
    private readonly string? _adminApiKey;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ConsoleRealtimeFallbackTracker _tracker;

    private HubConnection? _connection;
    private bool _stopping;

    public SignalRConsoleOpsHealthRealtimeClient(
        IConsoleEnvironmentProfileStore profileStore,
        IConsoleAccountSessionStore sessions,
        ILogger<SignalRConsoleOpsHealthRealtimeClient> logger,
        string? adminApiKey = null)
    {
        _profileStore = profileStore ?? throw new ArgumentNullException(nameof(profileStore));
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _adminApiKey = string.IsNullOrWhiteSpace(adminApiKey) ? null : adminApiKey;
        _tracker = new ConsoleRealtimeFallbackTracker(_logger, HubPath + "/" + SubscribeMethod);
    }

    /// <inheritdoc />
    public event Action<OpsHealthSnapshotResponse>? SnapshotReceived;

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
                // No environment bound: stay inert, exactly like the other admin-hub clients.
                _tracker.MarkNotConfigured();
                return;
            }

            connection.On<OpsHealthSnapshotResponse>(SnapshotEvent, snapshot => Raise(snapshot));

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
                    _tracker.MarkFallbackEngaged("Reconnected but re-subscribing to the ops-health group failed.", ex);
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
                // Expected against a server that predates honua-server PR #2591, or one running
                // without a Redis backplane (the group is only advertised with one): the hub
                // method does not exist, InvokeAsync throws, and the trend charts fall back to the
                // history-refresh poll — logged at the tracker's normal level, not an error
                // condition on our side.
                _tracker.MarkFallbackEngaged("Failed to connect or subscribe to the ops-health group.", ex);
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

    private void Raise(OpsHealthSnapshotResponse snapshot) => SnapshotReceived?.Invoke(snapshot);
}
