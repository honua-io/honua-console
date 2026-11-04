using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

// Hub transitions are at-least-once, so consumers dedupe on operation id and transition kind.
public sealed record ConsoleDeployOperationRealtimeEvent(
    string OperationId,
    string TransitionKind,
    string Status,
    string? CorrelationId,
    DateTimeOffset GeneratedAt);

public interface IConsoleDeployOperationRealtimeClient : IAsyncDisposable
{
    event Action<ConsoleDeployOperationRealtimeEvent>? OperationChanged;

    // A missing hub group becomes the fallback state and does not throw, so callers keep their poll loop.
    Task StartAsync(CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);
}

public static class ConsoleDeployOperationRealtimeEventExtensions
{
    public static OperateTimelineEntry ToTimelineEntry(this ConsoleDeployOperationRealtimeEvent evt) => new(
        Kind: "deploy-operation",
        Severity: evt.Status,
        Message: $"{evt.OperationId}: {evt.TransitionKind} -> {evt.Status}",
        Timestamp: evt.GeneratedAt.ToUniversalTime().ToString("u"),
        CorrelationId: evt.CorrelationId ?? string.Empty,
        OperationId: evt.OperationId,
        TransitionKind: evt.TransitionKind,
        DetailHref: CorrelationIdRoutes.Resolve(CorrelationIdKind.OperationId, evt.OperationId));
}
