namespace Honua.Console.Shell.Security;

// Validates operator- and server-supplied URLs before rendering them as clickable href attributes.
public static class ConsoleSafeUrl
{
    public static string? NormalizeHttpUrl(string? raw)
    {
        var trimmed = TrimSafeCharacters(raw);
        return trimmed is not null && IsSafeAbsoluteHttpUrl(trimmed) ? trimmed : null;
    }

    public static string? NormalizeHttpOrRelativeUrl(string? raw)
    {
        var trimmed = TrimSafeCharacters(raw);
        if (trimmed is null)
        {
            return null;
        }

        return trimmed.StartsWith('/')
            ? (IsSafeRelativePath(trimmed) ? trimmed : null)
            : (IsSafeAbsoluteHttpUrl(trimmed) ? trimmed : null);
    }

    private static string? TrimSafeCharacters(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var trimmed = raw.Trim();
        return trimmed.Contains('\\', StringComparison.Ordinal) || trimmed.Any(char.IsControl)
            ? null
            : trimmed;
    }

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
