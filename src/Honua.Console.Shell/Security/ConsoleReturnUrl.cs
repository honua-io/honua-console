namespace Honua.Console.Shell.Security;

/// <summary>
/// Shared sanitiser for operator-supplied <c>returnTo</c> redirect targets, used by both the Web
/// host's auth endpoints (<c>ConsoleAuthentication</c>) and the in-app sign-in page
/// (<c>AuthSignInPage</c>) so the open-redirect rule lives in exactly one place.
///
/// Only a site-relative path is allowed. A value is rejected (falling back to <c>"/"</c>) when it is
/// protocol-relative (<c>//host</c>), absolute (<c>scheme://host</c>), or contains a backslash:
/// browsers normalise <c>\</c> to <c>/</c> before navigating, so <c>/\evil.com</c> — which passes a
/// naive "starts with / but not //" check — becomes the protocol-relative external host
/// <c>//evil.com</c>. Rejecting any backslash closes that bypass.
/// </summary>
public static class ConsoleReturnUrl
{
    public static string Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || !value.StartsWith('/')
            || value.StartsWith("//", StringComparison.Ordinal)
            || value.Contains('\\', StringComparison.Ordinal)
            || value.Contains("://", StringComparison.Ordinal))
        {
            return "/";
        }

        return value;
    }
}
