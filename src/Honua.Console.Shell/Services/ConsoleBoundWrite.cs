using Honua.Console.Contracts;

namespace Honua.Console.Shell.Services;

internal static class ConsoleBoundWrite
{
    internal static async Task<HonuaAdminEndpointResult<T>> SendAsync<T>(
        Func<Task<HonuaAdminEndpointResult<T>>> send, Func<Task<bool>>? confirmAuthority)
    {
        HonuaAdminEndpointResult<T> result;
        try
        {
            result = await send().ConfigureAwait(false);
            if (confirmAuthority is not null && !await confirmAuthority().ConfigureAwait(false))
                return Unverified<T>();
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException)
        {
            return Unverified<T>();
        }

        if (confirmAuthority is not null && result.Data is null && result.Issue is { } issue
            && (issue.StatusCode is null or >= 200 and < 300 || issue.State == ConsoleWriteOutcome.UnverifiedState))
            return result with
            {
                Issue = issue with
                {
                    State = ConsoleWriteOutcome.UnverifiedState,
                    Detail = issue.State == ConsoleWriteOutcome.UnverifiedState ? issue.Detail
                        : ConsoleWriteOutcome.UnverifiedDetail + " " + issue.Detail
                }
            };
        return result;
    }

    private static HonuaAdminEndpointResult<T> Unverified<T>() => HonuaAdminEndpointResult<T>.FromIssue(
        new(ConsoleWriteOutcome.UnverifiedState, "Bound write", ConsoleWriteOutcome.UnverifiedDetail));
}
