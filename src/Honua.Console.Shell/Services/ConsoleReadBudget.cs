using System.Globalization;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Bounds an approval-critical read (console#308) so a surface of record can never stick on
/// "Loading…" forever when the honua-server admin API hangs. It races the read against a time
/// budget: a read that beats the budget returns its own result verbatim (success, empty, or a
/// missing/forbidden/unsupported denial), while a read that blows the budget — or throws — is
/// collapsed to an honest <see cref="OperateSectionStatus.Unavailable"/> result the surface
/// renders as its explicit error card (naming the source, offering Retry). The read's own
/// cancellation is best-effort-signalled when the budget elapses so a hung HTTP call is not
/// left running indefinitely.
///
/// This is the error/timeout companion to the repo's missing-binding convention: capability
/// absence (Missing/Forbidden/Unsupported) still flows through untouched; only a backend that
/// is reachable-but-broken or unreachable-and-hanging becomes the Unavailable error state.
/// </summary>
public static class ConsoleReadBudget
{
    /// <summary>The default approval-read budget (console#308: "~5s").</summary>
    public static readonly TimeSpan Default = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Runs <paramref name="read"/> under a <paramref name="budget"/>. Returns the read's own
    /// result when it completes in time; otherwise an <see cref="OperateSectionStatus.Unavailable"/>
    /// result carrying <paramref name="unreachableMessage"/> as the secondary status detail.
    /// </summary>
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

    /// <summary>
    /// Whether a denied read represents a backend-down / unreachable error (as opposed to a
    /// capability/permission absence that the missing-binding surface already covers). Only the
    /// error case gets the explicit error card with Retry.
    /// </summary>
    public static bool IsErrorState(OperateSectionStatus status) =>
        status == OperateSectionStatus.Unavailable;
}

/// <summary>
/// Shared, plain-language copy for the approval-surface trust affordances (console#308/#309):
/// the persistent "last successful refresh" caption and the degraded-freshness line. Kept in one
/// place so the inbox page, the home approval band, and the ops-summary health strip word these
/// identically.
/// </summary>
public static class ConsoleFreshness
{
    /// <summary>
    /// The persistent last-successful-refresh caption: the last good data time, or an explicit
    /// "never loaded" — so a failure never masquerades as "no data yet".
    /// </summary>
    public static string LastRefreshed(DateTimeOffset? lastSuccessUtc) =>
        lastSuccessUtc is { } at
            ? $"Last refreshed {FormatTime(at)}"
            : "Never loaded";

    /// <summary>The bare time (or "not yet") for inline use inside a longer sentence.</summary>
    public static string LastRefreshedShort(DateTimeOffset? lastSuccessUtc) =>
        lastSuccessUtc is { } at ? FormatTime(at) : "not yet";

    private static string FormatTime(DateTimeOffset at) =>
        at.ToUniversalTime().ToString("HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);
}
