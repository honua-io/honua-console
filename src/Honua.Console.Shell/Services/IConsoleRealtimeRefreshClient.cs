namespace Honua.Console.Shell.Services;

// A reset is acknowledged only after each mounted consumer has reconciled its REST projection.
public interface IConsoleRealtimeRefreshClient
{
    event Func<Task>? RefreshRequired;
}
