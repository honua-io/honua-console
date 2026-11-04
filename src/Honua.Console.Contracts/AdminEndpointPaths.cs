namespace Honua.Console.Contracts;

internal static class AdminEndpointPaths
{
    // A rooted path resolved against HttpClient.BaseAddress drops a base-path prefix such as
    // https://host/honua/ (RFC 3986 section 5.3), so resolve it against a slash-terminated base instead.
    public static Uri Resolve(Uri baseUri, string relativePath)
    {
        var normalizedBase = baseUri.AbsoluteUri.EndsWith('/')
            ? baseUri
            : new Uri(baseUri.AbsoluteUri + "/", UriKind.Absolute);
        var relative = relativePath.StartsWith('/') ? relativePath[1..] : relativePath;
        return new Uri(normalizedBase, relative);
    }
}
