namespace Honua.Console.Shell.Services;

// Collapse refresh bursts while awaiting every requested follow-up read.
public sealed class ConsoleRefreshCoalescer
{
    private readonly object _gate = new();
    private TaskCompletionSource? _completion;
    private bool _pending;

    public Task RunAsync(Func<Task> refresh)
    {
        TaskCompletionSource completion;
        lock (_gate)
        {
            _pending = true;
            if (_completion is not null) return _completion.Task;
            completion = _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        _ = DrainAsync(refresh, completion);
        return completion.Task;
    }

    private async Task DrainAsync(Func<Task> refresh, TaskCompletionSource completion)
    {
        try
        {
            while (true)
            {
                lock (_gate)
                {
                    if (!_pending)
                    {
                        _completion = null;
                        completion.SetResult();
                        return;
                    }
                    _pending = false;
                }
                await refresh();
            }
        }
        catch (Exception error)
        {
            lock (_gate)
            {
                _pending = false;
                _completion = null;
                completion.SetException(error);
            }
        }
    }
}
