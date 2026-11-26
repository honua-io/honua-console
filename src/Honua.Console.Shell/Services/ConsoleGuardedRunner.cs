using System.Net;
using System.Text.Json;
using Honua.Console.Contracts;

namespace Honua.Console.Shell.Services;

public static class ConsoleGuardedRunner
{
    private const string GenericFailure = "An unexpected error occurred. Try again.";

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
            // Surface an operator-friendly failure instead of raw runtime/network/parser details.
            notifications.Error($"{failureMessage}: {SummarizeFailure(ex)}");
            return false;
        }
        finally
        {
            setBusy?.Invoke(false);
        }
    }

    public static string SummarizeFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception switch
        {
            HttpRequestException http => SummarizeHttpFailure(http),
            OperationCanceledException => "The request timed out or was cancelled. Try again.",
            JsonException => "The server returned an unexpected response. Try again or contact your administrator.",
            IOException => "The file or data stream could not be read. Try again.",
            InvalidOperationException invalid => SanitizeOperatorMessage(invalid.Message),
            _ => GenericFailure
        };
    }

    private static string SummarizeHttpFailure(HttpRequestException exception)
    {
        var retryAfter = exception.Data[ConsoleFailureReceiptParser.RetryAfterSecondsDataKey] is double seconds
            ? seconds
            : (double?)null;

        return exception.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "Your sign-in expired. Sign in again and retry.",
            HttpStatusCode.Forbidden => "You do not have permission for this action.",
            HttpStatusCode.NotFound => "The requested item could not be found.",
            HttpStatusCode.Conflict or HttpStatusCode.PreconditionFailed or HttpStatusCode.PreconditionRequired =>
                "The item changed on the server. Reload and try again.",
            HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity =>
                "The server rejected the request. Review the inputs and try again.",
            HttpStatusCode.TooManyRequests =>
                $"Too many requests. {ConsoleFailureReceiptParser.FormatRetryWaitHint(retryAfter)}",
            HttpStatusCode.ServiceUnavailable => retryAfter is > 0
                ? $"The server is temporarily unavailable. {ConsoleFailureReceiptParser.FormatRetryWaitHint(retryAfter)}"
                : "The server is temporarily unavailable. Try again in a moment.",
            _ => "The server could not be reached or did not complete the request. Try again in a moment."
        };
    }

    private static string SanitizeOperatorMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return GenericFailure;
        if (message.Contains('\n') || message.Contains('\r')
            || message.Contains("   at ", StringComparison.Ordinal)
            || message.Contains("TypeError:", StringComparison.OrdinalIgnoreCase)
            || message.Contains("System.", StringComparison.Ordinal))
        {
            return GenericFailure;
        }

        return message.Trim();
    }
}
