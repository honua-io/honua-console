using Microsoft.AspNetCore.Components;

namespace Honua.Console.Shell.Services;

public static class ConsoleNavigationPaths
{
    /// <summary>Resolves a canonical Console route beneath the deployed application base.</summary>
    public static string ToConsoleHref(this NavigationManager navigation, string route)
    {
        ArgumentException.ThrowIfNullOrEmpty(route);
        if (!route.StartsWith('/') || route.StartsWith("//", StringComparison.Ordinal))
        {
            throw new ArgumentException("A Console route must start with a single slash.", nameof(route));
        }

        var target = navigation.ToAbsoluteUri(route[1..]);
        return target.PathAndQuery + target.Fragment;
    }
}
