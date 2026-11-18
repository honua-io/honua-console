using Microsoft.JSInterop;

namespace Honua.Console.Shell.Interop;

/// <summary>
/// Loads a same-origin browser module, and retries when the import does not settle.
/// An aborted fetch can leave the browser's module entry pending forever. A later import of the
/// same specifier waits on that entry, so a retry uses a new query. The first attempt keeps the
/// stable path the rest of the harness imports.
/// </summary>
public static class RetryingModuleImport
{
    public const int MaxAttempts = 3;
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    public static string Specifier(string path, int attempt)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(attempt, 1);
        return attempt == 1 ? path : $"{path}?honuaRetry={attempt}";
    }

    public static async Task<IJSObjectReference?> ImportAsync(
        IJSRuntime runtime,
        string path,
        int attempt,
        CancellationToken cancellationToken,
        TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        if (attempt > MaxAttempts)
        {
            return null;
        }

        // The analyzer requires the interop call itself to handle a failed runtime.
        // Callers choose whether that failure is retried.
        Task<IJSObjectReference> pending;
        try
        {
            pending = runtime.InvokeAsync<IJSObjectReference>("import", Specifier(path, attempt)).AsTask();
        }
        catch (JSDisconnectedException)
        {
            throw;
        }
        catch (JSException)
        {
            throw;
        }

        Task completed;
        try
        {
            completed = await Task.WhenAny(pending, Task.Delay(timeout ?? Timeout, cancellationToken));
        }
        catch (OperationCanceledException)
        {
            ObserveAbandoned(pending);
            throw;
        }

        if (completed != pending)
        {
            ObserveAbandoned(pending);
            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException("Browser module import did not finish.");
        }

        return await pending;
    }

    private static void ObserveAbandoned(Task<IJSObjectReference> pending)
    {
        _ = pending.ContinueWith(
            static task =>
            {
                if (task.Status == TaskStatus.RanToCompletion)
                {
                    _ = task.Result.DisposeAsync().AsTask();
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnRanToCompletion,
            TaskScheduler.Default);
    }
}
