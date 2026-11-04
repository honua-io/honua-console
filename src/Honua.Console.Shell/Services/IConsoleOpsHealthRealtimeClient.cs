using Honua.Console.Contracts;

namespace Honua.Console.Shell.Services;

public interface IConsoleOpsHealthRealtimeClient : IAsyncDisposable
{
    // Callbacks run on the connection thread, so a UI subscriber marshals its own update.
    event Action<OpsHealthSnapshotResponse>? SnapshotReceived;

    Task StartAsync(CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);
}
