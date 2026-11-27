namespace Honua.Console.Shell.Security;

// Validates operator- and server-supplied URLs before rendering them as clickable href attributes.
public static class ConsoleSafeUrl
{
    public static string? NormalizeHttpUrl(string? raw, bool allowRelativePath = false)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var trimmed = raw.Trim();
        if (HasUnsafeCharacters(trimmed))
        {
            return null;
        }

        if (allowRelativePath && trimmed.StartsWith('/'))
        {
            return IsSafeRelativePath(trimmed) ? trimmed : null;
        }

        return IsSafeAbsoluteHttpUrl(trimmed) ? trimmed : null;
    }

    private static bool HasUnsafeCharacters(string value) =>
        value.Contains('\\', StringComparison.Ordinal) || value.Any(char.IsControl);

    private static bool IsSafeRelativePath(string value) =>
        !value.StartsWith("//", StringComparison.Ordinal)
        && !value.Contains("://", StringComparison.Ordinal);

    private static bool IsSafeAbsoluteHttpUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
        && !string.IsNullOrEmpty(uri.Host)
        && string.IsNullOrEmpty(uri.UserInfo);
}
