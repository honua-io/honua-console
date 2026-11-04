using Microsoft.Extensions.Logging;

namespace Honua.Console.Shell.Services;

public sealed class ConsoleRealtimeFallbackTracker
{
    private readonly ILogger _logger;
    private readonly string _hubPath;

    public ConsoleRealtimeFallbackTracker(ILogger logger, string hubPath)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _hubPath = string.IsNullOrWhiteSpace(hubPath) ? "(unknown hub)" : hubPath;
    }

    public ConsoleRealtimeConnectionState State { get; private set; } = ConsoleRealtimeConnectionState.NotConfigured;

    public event Action<ConsoleRealtimeConnectionState>? StateChanged;

    public void MarkNotConfigured() => SetState(ConsoleRealtimeConnectionState.NotConfigured);

    public void MarkConnecting() => SetState(ConsoleRealtimeConnectionState.Connecting);

    public void MarkConnected() => SetState(ConsoleRealtimeConnectionState.Connected);

    public void MarkFallbackEngaged(string reason, Exception? exception = null)
    {
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
