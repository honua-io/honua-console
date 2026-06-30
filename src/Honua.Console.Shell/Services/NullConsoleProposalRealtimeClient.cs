using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Inert <see cref="IConsoleProposalRealtimeClient"/> for hosts/tests with no live hub
/// connection. It never connects and never raises events, so the inbox degrades to an
/// explicit manual refresh rather than fabricating proposal activity (Console Patterns
/// Charter section 11).
/// </summary>
public sealed class NullConsoleProposalRealtimeClient : IConsoleProposalRealtimeClient
{
    /// <inheritdoc />
    public event Action<ConsoleProposalEvent>? ProposalChanged
    {
        add { /* no live source — nothing to subscribe to */ }
        remove { /* no live source — nothing to unsubscribe from */ }
    }

    /// <inheritdoc />
    public bool IsConnected => false;

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <inheritdoc />
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
