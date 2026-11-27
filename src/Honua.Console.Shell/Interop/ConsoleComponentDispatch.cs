namespace Honua.Console.Shell.Interop;

internal static class ConsoleComponentDispatch
{
    public static async Task<bool> TryInvokeAsync(Func<Action, Task> invokeAsync, Action work)
    {
        try
        {
            await invokeAsync(work);
            return true;
        }
        catch (Exception exception) when (exception is ObjectDisposedException or InvalidOperationException or OperationCanceledException)
        {
            return false;
        }
    }
}
