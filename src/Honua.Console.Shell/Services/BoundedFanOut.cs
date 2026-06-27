namespace Honua.Console.Shell.Services;

/// <summary>
/// Runs an asynchronous operation across a collection with a hard concurrency cap, so a single
/// Operate page load cannot open hundreds of concurrent admin HTTP requests against honua-server
/// (a thundering-herd / connection-exhaustion cliff). Results are returned in source order.
/// </summary>
/// <remarks>
/// This mirrors the deliberate bound the Operate observability client applies for the same reason;
/// it is the one shared helper data sources route their admin-API fan-outs through.
/// </remarks>
internal static class BoundedFanOut
{
    /// <summary>
    /// Default maximum number of concurrent in-flight operations for an admin-API fan-out.
    /// </summary>
    public const int DefaultConcurrency = 8;

    /// <summary>
    /// Invokes <paramref name="body"/> for every element of <paramref name="source"/> with at most
    /// <paramref name="maxConcurrency"/> operations running at once, returning the results in the
    /// same order as the source. Returns an empty array for an empty source.
    /// </summary>
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
