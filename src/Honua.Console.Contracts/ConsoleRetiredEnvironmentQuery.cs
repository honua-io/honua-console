namespace Honua.Console.Contracts;

public static class ConsoleRetiredEnvironmentQuery
{
    public static bool HasRetiredKey(string uri)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uri);

        var parsed = new Uri(uri, UriKind.Absolute);
        return ConsoleUrlQuery.Parse(parsed.Query).Keys.Any(key =>
            string.Equals(key, "workspace", StringComparison.OrdinalIgnoreCase));
    }
}
