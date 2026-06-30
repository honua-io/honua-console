using Honua.Console.Shell.Models;
using Honua.Console.Shell.Security;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Live <see cref="IConsoleProposalRealtimeClient"/> backed by the honua-server admin
/// realtime hub (honua-server #1695). It connects from the console's server process to
/// <c>{server}/hubs/admin</c>, joins the <c>proposals</c> group via the hub's
/// <c>SubscribeToProposals</c> method, and projects each <c>ProposalPending</c> /
/// <c>ProposalResolved</c> payload onto a <see cref="ConsoleProposalEvent"/>.
///
/// Auth mirrors the Family-B REST clients: the operator's forwardable honua-server bearer is
/// used when present, otherwise the shared admin <c>X-API-Key</c> is sent. The hub is gated by
/// the same admin authorization as the REST proposals API. A connect failure (no environment
/// bound, server unreachable, hub unsupported) leaves the client disconnected and silent — the
/// inbox stays usable via manual refresh and never sees fabricated events.
/// </summary>
public sealed class SignalRConsoleProposalRealtimeClient : IConsoleProposalRealtimeClient
{
    internal const string HubPath = "hubs/admin";
    internal const string SubscribeMethod = "SubscribeToProposals";
    internal const string UnsubscribeMethod = "UnsubscribeFromProposals";
    internal const string PendingEvent = "ProposalPending";
    internal const string ResolvedEvent = "ProposalResolved";

    private readonly IConsoleEnvironmentProfileStore _profileStore;
    private readonly IConsoleAccountSessionStore _sessions;
    private readonly string? _adminApiKey;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private HubConnection? _connection;

    public SignalRConsoleProposalRealtimeClient(
        IConsoleEnvironmentProfileStore profileStore,
        IConsoleAccountSessionStore sessions,
        string? adminApiKey = null)
    {
        _profileStore = profileStore ?? throw new ArgumentNullException(nameof(profileStore));
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _adminApiKey = string.IsNullOrWhiteSpace(adminApiKey) ? null : adminApiKey;
    }

    /// <inheritdoc />
    public event Action<ConsoleProposalEvent>? ProposalChanged;

    /// <inheritdoc />
    public bool IsConnected => _connection?.State == HubConnectionState.Connected;

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

            var profile = await _profileStore.GetActiveProfileAsync(cancellationToken).ConfigureAwait(false);
            if (profile is null)
            {
                // No environment bound: stay inert. The inbox renders the missing-binding
                // state from its REST read; there is nothing to subscribe to.
                return;
            }

            var bearer = await ConsoleServerHttp
                .ResolveForwardableBearerAsync(_sessions, profile, cancellationToken)
                .ConfigureAwait(false);

            var hubUri = ConsoleServerHttp.BuildUri(profile.ServerBaseUri, HubPath);

            var connection = new HubConnectionBuilder()
                .WithUrl(hubUri, options =>
                {
                    if (!string.IsNullOrWhiteSpace(_adminApiKey))
                    {
                        options.Headers["X-API-Key"] = _adminApiKey;
                    }

                    if (!string.IsNullOrWhiteSpace(bearer) && !ConsoleAuthConstants.IsSessionSentinel(bearer))
                    {
                        options.AccessTokenProvider = () => Task.FromResult<string?>(bearer);
                    }
                })
                .WithAutomaticReconnect()
                .AddJsonProtocol(o => o.PayloadSerializerOptions.PropertyNameCaseInsensitive = true)
                .Build();

            connection.On<ProposalRealtimeWire>(
                PendingEvent,
                wire => Raise(ConsoleProposalEventKind.Pending, wire));
            connection.On<ProposalRealtimeWire>(
                ResolvedEvent,
                wire => Raise(ConsoleProposalEventKind.Resolved, wire));

            // Re-join the proposals group after an automatic reconnect, else the connection
            // would be live but no longer in the group and would silently miss events.
            connection.Reconnected += async _ =>
            {
                try
                {
                    await connection.InvokeAsync(SubscribeMethod).ConfigureAwait(false);
                }
                catch
                {
                    // Best-effort re-subscribe; the next manual refresh still reconciles state.
                }
            };

            try
            {
                await connection.StartAsync(cancellationToken).ConfigureAwait(false);
                await connection.InvokeAsync(SubscribeMethod, cancellationToken).ConfigureAwait(false);
                _connection = connection;
            }
            catch
            {
                // Best-effort: a connect/subscribe failure leaves us disconnected and silent.
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
        try
        {
            if (_connection is null)
            {
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
            catch
            {
                // Best-effort unsubscribe; disposing the connection drops the group membership anyway.
            }

            await connection.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
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
