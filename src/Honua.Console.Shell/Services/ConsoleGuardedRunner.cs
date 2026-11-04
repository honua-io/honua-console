namespace Honua.Console.Shell.Services;

public static class ConsoleGuardedRunner
{
    public static async Task<bool> RunGuardedAsync(
        this IConsoleNotificationService notifications,
        Func<Task> operation,
        Action<bool>? setBusy = null,
        string failureMessage = "Something went wrong",
        string? onSuccess = null)
    {
        ArgumentNullException.ThrowIfNull(notifications);
        ArgumentNullException.ThrowIfNull(operation);

        setBusy?.Invoke(true);
        try
        {
            await operation();
            if (!string.IsNullOrWhiteSpace(onSuccess))
            {
                notifications.Success(onSuccess);
            }

            return true;
        }
        catch (Exception ex)
        {
            // Surface the failure instead of letting it disappear when the spinner resets. The message
            // stays concise; the full exception is the operation's own concern (logged server-side).
            notifications.Error($"{failureMessage}: {ex.Message}");
            return false;
        }
        finally
        {
            setBusy?.Invoke(false);
        }
    }
}
