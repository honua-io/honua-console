using System.Globalization;
using Microsoft.JSInterop;

namespace Honua.Console.Shell.Security;

// The server returns from identity-provider sign-in only to a fixed set of Console pages, so the page
// the operator asked for waits in this tab's session storage and is restored once after sign-in.
public static class ConsolePendingReturn
{
    private const string StorageKey = "honua.console.pendingReturn";
    internal static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(10);

    // Best effort: with browser storage blocked or full, sign-in goes ahead and returns to Home.
    public static async Task SaveAsync(IJSRuntime js, string route, DateTimeOffset now)
    {
        try
        {
            await js.InvokeVoidAsync("sessionStorage.setItem", StorageKey,
                string.Create(CultureInfo.InvariantCulture, $"{now.ToUnixTimeMilliseconds()}|{route}"));
        }
        catch (JSException)
        {
        }
    }

    // Reads and removes the saved page; returns it only while it is fresh and still a safe local route.
    public static async Task<string?> TakeAsync(IJSRuntime js, DateTimeOffset now)
    {
        string? saved;
        try
        {
            saved = await js.InvokeAsync<string?>("sessionStorage.getItem", StorageKey);
            if (saved is null)
            {
                return null;
            }

            await js.InvokeVoidAsync("sessionStorage.removeItem", StorageKey);
        }
        catch (JSException)
        {
            return null;
        }

        var separator = saved.IndexOf('|', StringComparison.Ordinal);
        if (separator <= 0
            || !long.TryParse(saved.AsSpan(0, separator), NumberStyles.None, CultureInfo.InvariantCulture, out var savedAt)
            || savedAt > now.ToUnixTimeMilliseconds())
        {
            return null;
        }

        var age = TimeSpan.FromMilliseconds(now.ToUnixTimeMilliseconds() - savedAt);
        var route = ConsoleReturnUrl.Sanitize(saved[(separator + 1)..]);
        return age <= MaxAge && route != "/" ? route : null;
    }
}
