namespace Honua.Console.Shell.Services;

// FallbackEngaged means a connect or resume failed; NotConfigured means no environment is bound, and that state is not logged as a failure.
public enum ConsoleRealtimeConnectionState
{
    NotConfigured,

    Connecting,

    Connected,

    FallbackEngaged,
}
