namespace Honua.Console.Shell.Services;

public interface IConsoleRealtimeCapabilityClient : IAsyncDisposable
{
    ConsoleRealtimeConnectionState ConnectionState { get; }

    event Action<ConsoleRealtimeConnectionState>? ConnectionStateChanged;

    bool IsConnected => ConnectionState == ConsoleRealtimeConnectionState.Connected;

    bool IsFallbackEngaged => ConnectionState == ConsoleRealtimeConnectionState.FallbackEngaged;

    Task StartAsync(CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);
}
