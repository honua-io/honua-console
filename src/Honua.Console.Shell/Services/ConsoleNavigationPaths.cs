using Microsoft.AspNetCore.Components;

namespace Honua.Console.Shell.Services;

public static class ConsoleNavigationPaths
{
    public static string ToConsoleHref(this NavigationManager navigation, string route)
    {
        ArgumentException.ThrowIfNullOrEmpty(route);
        if (!route.StartsWith('/') || route.StartsWith("//", StringComparison.Ordinal))
        {
            throw new ArgumentException("A Console route must start with a single slash.", nameof(route));
        }

        if (!navigation.TryGetConsoleHref(route, out var href))
            throw new ArgumentException("The destination must stay within the Console application.", nameof(route));
        var target = navigation.ToAbsoluteUri(href);
        return target.PathAndQuery + target.Fragment;
    }

    public static bool TryGetConsoleHref(this NavigationManager navigation, string? target, out string href)
    {
        href = string.Empty;
        if (string.IsNullOrWhiteSpace(target) || target.Any(char.IsControl) || target.Contains('\\'))
            return false;
        if (!Uri.TryCreate(navigation.BaseUri, UriKind.Absolute, out var appBase)
            || appBase.Scheme is not ("http" or "https") || !appBase.AbsolutePath.EndsWith('/'))
            return false;

        var route = target;
        if (route.StartsWith("//", StringComparison.Ordinal)) return false;
        if (Uri.TryCreate(route, UriKind.Absolute, out var absolute) && !route.StartsWith('/'))
        {
            if (!appBase.IsBaseOf(absolute)) return false;
            route = appBase.MakeRelativeUri(absolute).ToString();
        }
        else if (route.StartsWith(appBase.AbsolutePath, StringComparison.Ordinal))
        {
            route = route[appBase.AbsolutePath.Length..];
        }
        else
        {
            route = route.TrimStart('/');
        }

        if (route.StartsWith("./", StringComparison.Ordinal)) route = route[2..];

        // Validate the decoded path as well: encoded separators or dot segments must not escape the mount.
        var path = Uri.UnescapeDataString(route.Split('?', '#')[0]);
        if (path.Contains('\\') || path.Contains(':') || path.StartsWith('/')
            || path.Any(char.IsControl) || path.Split('/').Any(segment => segment is "." or ".."))
            return false;
        if (!Uri.TryCreate(appBase, route, out var resolved) || !appBase.IsBaseOf(resolved))
            return false;

        href = "./" + route;
        return true;
    }
}
