using Honua.Console.Shell.Services;
using Microsoft.Extensions.Logging;

namespace Honua.Console.Native.Core.Tests;

/// <summary>
/// Coverage for the console#293 shared realtime seam's fallback-engagement surfacing — the fix
/// for finding PA-233 (<c>SignalRConsoleProposalRealtimeClient</c> used to swallow connect/
/// subscribe/reconnect failures in a bare <c>catch {}</c> with no logger, so a live surface could
/// silently degrade to its manual/poll fallback). <see cref="ConsoleRealtimeFallbackTracker"/> is
/// the one place that is now allowed to happen, and it must always log it and expose it via
/// <see cref="ConsoleRealtimeConnectionState"/>.
/// </summary>
public sealed class ConsoleRealtimeFallbackTrackerTests
{
    [Fact]
    public void StartsNotConfigured_AndMarkingItLogsNothing()
    {
        var logger = new RecordingLogger();
        var tracker = new ConsoleRealtimeFallbackTracker(logger, "hubs/admin");

        Assert.Equal(ConsoleRealtimeConnectionState.NotConfigured, tracker.State);

        tracker.MarkNotConfigured();

        Assert.Equal(ConsoleRealtimeConnectionState.NotConfigured, tracker.State);
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public void MarkFallbackEngaged_LogsAWarningWithTheCauseAndHubPath_AndTransitionsState()
    {
        var logger = new RecordingLogger();
        var tracker = new ConsoleRealtimeFallbackTracker(logger, "hubs/admin");
        var states = new List<ConsoleRealtimeConnectionState>();
        tracker.StateChanged += states.Add;

        var causeException = new InvalidOperationException("hub unreachable");
        tracker.MarkFallbackEngaged("Failed to connect or subscribe to the proposals group.", causeException);

        // PA-233: this used to be silent. It is now both logged and observable via state.
        Assert.Equal(ConsoleRealtimeConnectionState.FallbackEngaged, tracker.State);
        Assert.Equal([ConsoleRealtimeConnectionState.FallbackEngaged], states);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Same(causeException, entry.Exception);
        Assert.Contains("hubs/admin", entry.Message, StringComparison.Ordinal);
        Assert.Contains("Failed to connect or subscribe to the proposals group.", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MarkConnected_AfterFallback_ClearsTheStateAndRaisesAnotherTransition()
    {
        var logger = new RecordingLogger();
        var tracker = new ConsoleRealtimeFallbackTracker(logger, "hubs/admin");
        tracker.MarkFallbackEngaged("initial failure");

        var states = new List<ConsoleRealtimeConnectionState>();
        tracker.StateChanged += states.Add;
        tracker.MarkConnected();

        Assert.Equal(ConsoleRealtimeConnectionState.Connected, tracker.State);
        Assert.Equal([ConsoleRealtimeConnectionState.Connected], states);
    }

    [Fact]
    public void RepeatedSameState_DoesNotRaiseADuplicateTransition()
    {
        var logger = new RecordingLogger();
        var tracker = new ConsoleRealtimeFallbackTracker(logger, "hubs/admin");
        tracker.MarkConnected();

        var raiseCount = 0;
        tracker.StateChanged += _ => raiseCount++;
        tracker.MarkConnected();

        Assert.Equal(0, raiseCount);
    }

    private sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);

    private sealed class RecordingLogger : ILogger
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add(new LogEntry(logLevel, formatter(state, exception), exception));
    }
}
