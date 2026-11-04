using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public interface IConsoleProposalRealtimeClient : IAsyncDisposable
{
    // Callbacks run on the connection thread, so a UI subscriber marshals its own update.
    event Action<ConsoleProposalEvent>? ProposalChanged;

    bool IsConnected { get; }

    bool IsFallbackEngaged => false;

    Task StartAsync(CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);
}
