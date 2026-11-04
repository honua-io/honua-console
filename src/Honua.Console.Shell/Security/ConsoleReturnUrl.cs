namespace Honua.Console.Shell.Security;

// Browsers treat a backslash as a slash, so a value like /\host would pass a starts-with-/ check and leave the site.
public static class ConsoleReturnUrl
{
    public static string Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || !value.StartsWith('/')
            || value.StartsWith("//", StringComparison.Ordinal)
            || value.Contains('\\', StringComparison.Ordinal)
            || value.Any(static character => character < ' ' || character == '\u007f')
            || value.Contains("://", StringComparison.Ordinal))
        {
            return "/";
        }

        return value;
    }
}
