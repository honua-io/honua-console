namespace Honua.Console.Shell.Tests.Components;

internal sealed class ManualTimeProvider(TimeSpan interval) : TimeProvider
{
    private TimerCallback? _callback;
    private object? _state;
    public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;

    public override DateTimeOffset GetUtcNow() => UtcNow;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        Assert.Equal(interval, dueTime);
        Assert.Equal(interval, period);
        _callback = callback;
        _state = state;
        return new ManualTimer();
    }

    public void Tick() => _callback!(_state);

    private sealed class ManualTimer : ITimer
    {
        public bool Change(TimeSpan dueTime, TimeSpan period) => true;
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

