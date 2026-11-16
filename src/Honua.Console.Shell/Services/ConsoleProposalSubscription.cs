using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public sealed record ConsoleProposalObservers(
    Action<ConsoleProposalEvent> ProposalChanged,
    Action<ConsoleRealtimeConnectionState> ConnectionChanged,
    Func<Task>? RefreshRequired = null);

public sealed class ConsoleProposalSubscription : IDisposable
{
    private readonly IConsoleProposalRealtimeClient? _client;
    private readonly IConsoleRealtimeCapabilityClient? _connection;
    private readonly ConsoleProposalObservers _observers;
    private readonly IConsoleRealtimeRefreshClient? _refresh;
    private bool _disposed;

    public ConsoleProposalSubscription(IConsoleProposalRealtimeClient? client, ConsoleProposalObservers observers)
    {
        _client = client;
        _observers = observers;
        if (client is null) return;
        client.ProposalChanged += observers.ProposalChanged;
        _refresh = client as IConsoleRealtimeRefreshClient;
        if (_refresh is not null && observers.RefreshRequired is not null) _refresh.RefreshRequired += observers.RefreshRequired;
        _connection = client as IConsoleRealtimeCapabilityClient;
        if (_connection is not null) _connection.ConnectionStateChanged += observers.ConnectionChanged;
    }

    public bool IsConnected => _client?.IsConnected == true;
    public bool IsFallbackEngaged => _client?.IsFallbackEngaged == true;

    public async Task StartAsync(CancellationToken token)
    {
        if (_disposed || _client is null) return;
        try { await _client.StartAsync(token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
        catch { /* Bounded reads and manual refresh remain available without a realtime subscription. */ }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_client is not null) _client.ProposalChanged -= _observers.ProposalChanged;
        if (_refresh is not null && _observers.RefreshRequired is not null) _refresh.RefreshRequired -= _observers.RefreshRequired;
        if (_connection is not null) _connection.ConnectionStateChanged -= _observers.ConnectionChanged;
    }
}
