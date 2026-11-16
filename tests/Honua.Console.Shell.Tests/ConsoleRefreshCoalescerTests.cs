using Honua.Console.Shell.Services;

namespace Honua.Console.Shell.Tests;

public sealed class ConsoleRefreshCoalescerTests
{
    [Fact]
    public async Task Burst_QueuesOneFollowUp_AndResetWaitsForItsCompletion()
    {
        var queue = new ConsoleRefreshCoalescer();
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = 0;
        async Task ReadAsync()
        {
            if (Interlocked.Increment(ref reads) == 1) await first.Task;
            else
            {
                secondStarted.SetResult();
                await second.Task;
            }
        }

        var active = queue.RunAsync(ReadAsync);
        var notifications = Enumerable.Range(0, 100).Select(_ => queue.RunAsync(ReadAsync)).ToArray();
        var reset = queue.RunAsync(ReadAsync);
        Assert.Equal(1, reads);
        first.SetResult();
        await secondStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, reads);
        Assert.False(reset.IsCompleted);
        second.SetResult();
        await Task.WhenAll(notifications.Append(active).Append(reset));
        Assert.Equal(2, reads);
    }

    [Fact]
    public async Task FailedRead_FailsAllWaiters_AndNextRequestCanRetry()
    {
        var queue = new ConsoleRefreshCoalescer();
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = queue.RunAsync(() => pending.Task);
        var reset = queue.RunAsync(() => Task.CompletedTask);
        pending.SetException(new InvalidOperationException("Read unavailable"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => active);
        await Assert.ThrowsAsync<InvalidOperationException>(() => reset);
        var retried = false;
        await queue.RunAsync(() => { retried = true; return Task.CompletedTask; });
        Assert.True(retried);
    }
}
