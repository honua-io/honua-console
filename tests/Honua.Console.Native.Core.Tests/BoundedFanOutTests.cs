using Honua.Console.Shell.Services;

namespace Honua.Console.Native.Core.Tests;

public sealed class BoundedFanOutTests
{
    [Fact]
    public async Task RunAsync_PreservesSourceOrder()
    {
        var source = Enumerable.Range(0, 50).ToArray();

        var results = await BoundedFanOut.RunAsync(
            source,
            async (value, _) =>
            {
                // Stagger so a naive unordered collection would reorder results.
                await Task.Delay(value % 5).ConfigureAwait(false);
                return value * 2;
            });

        Assert.Equal(source.Select(value => value * 2).ToArray(), results);
    }

    [Fact]
    public async Task RunAsync_NeverExceedsConcurrencyCap()
    {
        const int cap = 4;
        var inFlight = 0;
        var observedPeak = 0;
        var lockObject = new object();

        await BoundedFanOut.RunAsync(
            Enumerable.Range(0, 100).ToArray(),
            async (_, _) =>
            {
                lock (lockObject)
                {
                    inFlight++;
                    observedPeak = Math.Max(observedPeak, inFlight);
                }

                await Task.Delay(2).ConfigureAwait(false);

                lock (lockObject)
                {
                    inFlight--;
                }

                return 0;
            },
            maxConcurrency: cap);

        Assert.True(observedPeak <= cap, $"peak concurrency {observedPeak} exceeded cap {cap}");
    }

    [Fact]
    public async Task RunAsync_EmptySource_ReturnsEmptyWithoutInvokingBody()
    {
        var invoked = false;

        var results = await BoundedFanOut.RunAsync(
            Array.Empty<int>(),
            (_, _) =>
            {
                invoked = true;
                return Task.FromResult(0);
            });

        Assert.Empty(results);
        Assert.False(invoked);
    }
}
