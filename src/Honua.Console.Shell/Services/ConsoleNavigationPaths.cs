using Microsoft.AspNetCore.Components;

namespace Honua.Console.Shell.Services;

public static class ConsoleNavigationPaths
{
    public static string ToConsoleContextRoute(this NavigationManager navigation, string route)
    {
        var href = navigation.GetConsoleHref(route);
        if (href is null)
            throw new ArgumentException("The context route must stay within the Console application.", nameof(route));
        return "/" + href[2..].Split('?', '#')[0];
    }

    public static string ToConsoleHref(this NavigationManager navigation, string route)
    {
        ArgumentException.ThrowIfNullOrEmpty(route);
        if (!route.StartsWith('/') || route.StartsWith("//", StringComparison.Ordinal))
        {
            throw new ArgumentException("A Console route must start with a single slash.", nameof(route));
        }

        var href = navigation.GetConsoleHref(route);
        if (href is null)
            throw new ArgumentException("The destination must stay within the Console application.", nameof(route));
        var target = navigation.ToAbsoluteUri(href);
        return target.PathAndQuery + target.Fragment;
    }

    public static string? ToConsoleLinkHref(this NavigationManager navigation, string? target)
    {
        if (target is null || !target.StartsWith('/') || target.StartsWith("//", StringComparison.Ordinal))
            return target;

        // Mixed evidence and action lists can also contain server API and protocol URLs.
        // Only application route families belong under the Console mount.
        var family = target.Split('/', '?', '#')[1];
        var isConsoleRoute = family is "" or "approvals" or "catalog" or "catalogs" or "embed"
            or "environments" or "inbox" or "maps" or "metadata" or "not-found" or "operate"
            or "public" or "setup" or "share" or "studio" or "support"
            || target.Split('?', '#')[0] == "/auth/signin";
        return isConsoleRoute ? navigation.ToConsoleHref(target) : target;
    }

    public static string? GetConsoleHref(this NavigationManager navigation, string? target)
    {
        if (string.IsNullOrWhiteSpace(target) || target.Any(char.IsControl) || target.Contains('\\'))
            return null;
        if (!Uri.TryCreate(navigation.BaseUri, UriKind.Absolute, out var appBase)
            || appBase.Scheme is not ("http" or "https") || !appBase.AbsolutePath.EndsWith('/'))
            return null;

        var route = target;
        if (route.StartsWith("//", StringComparison.Ordinal)) return null;
        if (Uri.TryCreate(route, UriKind.Absolute, out var absolute) && !route.StartsWith('/'))
        {
            if (!appBase.IsBaseOf(absolute)) return null;
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
        if (path.Contains('\\') || path.StartsWith('/')
            || path.Any(char.IsControl) || path.Split('/').Any(segment => segment is "." or ".."))
            return null;
        if (!Uri.TryCreate(appBase, route, out var resolved) || !appBase.IsBaseOf(resolved))
            return null;

        return "./" + route;
    }
}
