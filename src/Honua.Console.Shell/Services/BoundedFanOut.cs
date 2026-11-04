namespace Honua.Console.Shell.Services;

// The concurrency cap stops one Operate page load from opening hundreds of admin HTTP calls.
internal static class BoundedFanOut
{
    public const int DefaultConcurrency = 8;

    public static async Task<TResult[]> RunAsync<TSource, TResult>(
        IReadOnlyList<TSource> source,
        Func<TSource, CancellationToken, Task<TResult>> body,
        int maxConcurrency = DefaultConcurrency,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(body);

        if (source.Count == 0)
        {
            return [];
        }

        var effective = Math.Max(1, Math.Min(maxConcurrency, source.Count));
        using var gate = new SemaphoreSlim(effective, effective);
        var results = new TResult[source.Count];

        async Task RunOneAsync(int index)
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                results[index] = await body(source[index], cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
            }
        }

        var tasks = new Task[source.Count];
        for (var i = 0; i < source.Count; i++)
        {
            tasks[i] = RunOneAsync(i);
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
        return results;
    }
}
