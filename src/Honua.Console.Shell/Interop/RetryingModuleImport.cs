using Microsoft.JSInterop;

namespace Honua.Console.Shell.Interop;

public enum ModuleImportScriptError
{
    Stop,
    Retry,
}

public sealed class RetryingModuleImport
{
    public const int MaxAttempts = 3;
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);
    private static int _retryNonce;

    private readonly ModuleImportScriptError _scriptErrors;
    private readonly TimeSpan _timeout;
    private int _attempt;

    public RetryingModuleImport(ModuleImportScriptError scriptErrors, TimeSpan? timeout = null)
    {
        _scriptErrors = scriptErrors;
        _timeout = timeout ?? Timeout;
    }

    public async Task<IJSObjectReference?> LoadAsync(
        IJSRuntime runtime,
        string path,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        while (_attempt < MaxAttempts)
        {
            try
            {
                return await ImportOnceAsync(runtime, path, cancellationToken);
            }
            catch (TimeoutException)
            {
            }
            catch (JSException) when (_scriptErrors == ModuleImportScriptError.Retry)
            {
            }
            catch (JSException)
            {
                return null;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        return null;
    }

    private async Task<IJSObjectReference> ImportOnceAsync(
        IJSRuntime runtime,
        string path,
        CancellationToken cancellationToken)
    {
        var attempt = ++_attempt;
        // The first try keeps the stable path. A later try uses a new query because an
        // unfinished or failed import stays recorded for that specifier.
        var specifier = attempt == 1
            ? path
            : $"{path}?honuaRetry={Interlocked.Increment(ref _retryNonce)}";

        cancellationToken.ThrowIfCancellationRequested();
        Task<IJSObjectReference> pending;
        try
        {
            pending = runtime.InvokeAsync<IJSObjectReference>("import", specifier).AsTask();
        }
        catch (JSDisconnectedException)
        {
            throw;
        }
        catch (JSException)
        {
            throw;
        }

        var completed = await Task.WhenAny(pending, Task.Delay(_timeout, cancellationToken));
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
                if (task.IsFaulted)
                {
                    _ = task.Exception;
                    return;
                }

                _ = task.Result.DisposeAsync().AsTask();
            },
            CancellationToken.None,
            TaskContinuationOptions.NotOnCanceled,
            TaskScheduler.Default);
    }
}
