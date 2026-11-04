using System.Globalization;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

// A finished read is returned unchanged, including a missing, forbidden, or unsupported denial; only a timeout or a throw becomes Unavailable.
public static class ConsoleReadBudget
{
    public static readonly TimeSpan Default = TimeSpan.FromSeconds(5);

    public static async Task<OperateSectionResult<T>> RunAsync<T>(
        Func<CancellationToken, Task<OperateSectionResult<T>>> read,
        TimeSpan budget,
        string unreachableMessage,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(read);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        Task<OperateSectionResult<T>> readTask;
        try
        {
            readTask = read(cts.Token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return OperateSectionResult<T>.Denied(OperateSectionStatus.Unavailable, unreachableMessage);
        }

        var winner = await Task.WhenAny(readTask, Task.Delay(budget, cancellationToken)).ConfigureAwait(false);
        if (winner != readTask)
        {
            // The read blew the budget. Best-effort cancel the hung call so it does not run on
            // forever, and surface the bounded error state rather than an endless spinner.
            cts.Cancel();
            return OperateSectionResult<T>.Denied(OperateSectionStatus.Unavailable, unreachableMessage);
        }

        try
        {
            return await readTask.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The read faulted (or was cancelled): the same honest, retryable error state.
            return OperateSectionResult<T>.Denied(OperateSectionStatus.Unavailable, unreachableMessage);
        }
    }

    // Only a backend-down or unreachable status gets the error card with Retry; missing, forbidden, and unsupported stay on the capability surface.
    public static bool IsErrorState(OperateSectionStatus status) =>
        status == OperateSectionStatus.Unavailable;
}

public static class ConsoleFreshness
{
    public static string LastRefreshed(DateTimeOffset? lastSuccessUtc) =>
        lastSuccessUtc is { } at
            ? $"Last refreshed {FormatTime(at)}"
            : "Never loaded";

    public static string LastRefreshedShort(DateTimeOffset? lastSuccessUtc) =>
        lastSuccessUtc is { } at ? FormatTime(at) : "not yet";

    private static string FormatTime(DateTimeOffset at) =>
        at.ToUniversalTime().ToString("HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);
}
