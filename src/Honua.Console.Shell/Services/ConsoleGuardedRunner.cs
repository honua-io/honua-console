namespace Honua.Console.Shell.Services;

/// <summary>
/// Shared error-surfacing wrapper for the "set busy → call the server → reset busy" pattern repeated
/// across Console pages. Before this helper most pages set a busy flag in a <c>try/finally</c> but had
/// no <c>catch</c>, so a thrown server error reset the spinner and vanished with no user feedback.
///
/// <see cref="RunGuardedAsync"/> wraps the operation: it flips the caller's busy flag on, runs the work
/// inside a try/catch that routes any failure to a toast through <see cref="IConsoleNotificationService"/>,
/// and always resets busy in the finally. Callers pass a <c>setBusy</c> delegate (usually
/// <c>busy =&gt; { _busy = busy; StateHasChanged(); }</c>) so the helper drives the disable-while-busy state.
/// </summary>
public static class ConsoleGuardedRunner
{
    /// <summary>
    /// Runs <paramref name="operation"/> with busy gating and toast-on-failure error surfacing.
    /// </summary>
    /// <param name="notifications">The shell notification surface failures are routed to.</param>
    /// <param name="operation">The work to run (typically a server call).</param>
    /// <param name="setBusy">
    /// Invoked with <c>true</c> before the operation and <c>false</c> in the finally so the caller can
    /// drive its busy flag (and disable submit controls). Optional.
    /// </param>
    /// <param name="failureMessage">
    /// Prefix for the error toast (e.g. "Couldn't publish the layer"). The exception message is appended.
    /// </param>
    /// <param name="onSuccess">Optional success toast text; shown only when the operation completes.</param>
    /// <returns><c>true</c> when the operation completed without throwing; <c>false</c> when it failed.</returns>
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
