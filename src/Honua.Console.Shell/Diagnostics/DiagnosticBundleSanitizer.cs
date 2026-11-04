using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Honua.Console.Contracts;

namespace Honua.Console.Shell.Diagnostics;

public static partial class DiagnosticBundleSanitizer
{
    // Conservative allowlist of non-secret headers. Anything not listed here is dropped, so a new
    // or unexpected header can never leak a secret into a bundle (allowlist, not denylist).
    private static readonly HashSet<string> AllowedHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "accept",
        "accept-encoding",
        "accept-language",
        "cache-control",
        "content-encoding",
        "content-language",
        "content-length",
        "content-type",
        "date",
        "etag",
        "retry-after",
        "traceparent",
        "tracestate",
        "user-agent",
        "vary",
        "x-correlation-id",
        "x-request-id",
        "x-trace-id",
    };

    // Header names that must NEVER appear even if some future edit widens the allowlist
    // (defense in depth). Matched case-insensitively as a substring.
    private static readonly string[] DeniedHeaderFragments =
    [
        "authorization",
        "cookie",
        "api-key",
        "apikey",
        "token",
        "secret",
        "password",
        "credential",
        "x-honua-key",
        "www-authenticate",
    ];

    private const int MaxPreviewLength = 4096;

    public static IReadOnlyList<DiagnosticHeader>? SanitizeHeaders(IEnumerable<DiagnosticHeader>? rawHeaders)
    {
        if (rawHeaders is null)
            return null;

        List<DiagnosticHeader> safe = [];
        foreach (DiagnosticHeader header in rawHeaders)
        {
            if (string.IsNullOrWhiteSpace(header.Name))
                continue;
            if (!IsSafeHeader(header.Name))
                continue;
            safe.Add(new DiagnosticHeader(header.Name.Trim(), (header.Value ?? string.Empty).Trim()));
        }

        return safe.Count == 0 ? null : safe;
    }

    public static bool IsSafeHeader(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        string trimmed = name.Trim();
        foreach (string denied in DeniedHeaderFragments)
        {
            if (trimmed.Contains(denied, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return AllowedHeaders.Contains(trimmed);
    }

    // Query values can carry tokens, so every value is replaced.
    public static string NormalizePath(string pathAndQuery)
    {
        if (string.IsNullOrWhiteSpace(pathAndQuery))
            return "/";

        string path = pathAndQuery.Trim();
        int queryIndex = path.IndexOf('?', StringComparison.Ordinal);
        if (queryIndex < 0)
            return path;

        string rawPath = path[..queryIndex];
        string query = path[(queryIndex + 1)..];
        if (query.Length == 0)
            return rawPath;

        IEnumerable<string> normalizedPairs = query
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair =>
            {
                int eq = pair.IndexOf('=', StringComparison.Ordinal);
                string key = eq < 0 ? pair : pair[..eq];
                return $"{key}={{value}}";
            });

        return $"{rawPath}?{string.Join('&', normalizedPairs)}";
    }

    public static DiagnosticBodyPreview? RedactBody(string? rawBody)
    {
        if (rawBody is null)
            return null;

        byte[] originalBytes = Encoding.UTF8.GetBytes(rawBody);
        string contentSha = Convert.ToHexStringLower(SHA256.HashData(originalBytes));

        string redacted = RedactSecrets(rawBody, out bool redactionApplied);

        bool truncated = false;
        if (redacted.Length > MaxPreviewLength)
        {
            redacted = redacted[..MaxPreviewLength];
            truncated = true;
        }

        return new DiagnosticBodyPreview
        {
            Preview = redacted,
            ContentSha256 = contentSha,
            OriginalByteSize = originalBytes.LongLength,
            RedactionApplied = redactionApplied,
            Truncated = truncated,
        };
    }

    private static string RedactSecrets(string input, out bool redactionApplied)
    {
        string result = SecretValuePattern().Replace(input, match =>
        {
            string prefix = match.Groups["prefix"].Value;
            return $"{prefix}\"[REDACTED]\"";
        });

        result = BearerPattern().Replace(result, "Bearer [REDACTED]");
        result = JwtPattern().Replace(result, "[REDACTED]");

        redactionApplied = !string.Equals(result, input, StringComparison.Ordinal);
        return result;
    }

    // JSON-ish secret fields: "password": "...", "token": "...", "apiKey": "...", "secret": "...".
    [GeneratedRegex(
        "(?<prefix>\"(?:[^\"]*(?:password|token|secret|apikey|api_key|authorization|credential|key)[^\"]*)\"\\s*:\\s*)\"(?:[^\"\\\\]|\\\\.)*\"",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SecretValuePattern();

    [GeneratedRegex("Bearer\\s+[A-Za-z0-9._~+/=-]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BearerPattern();

    // Bare JWT-looking triples (header.payload.signature).
    [GeneratedRegex("\\b[A-Za-z0-9_-]{10,}\\.[A-Za-z0-9_-]{10,}\\.[A-Za-z0-9_-]{10,}\\b", RegexOptions.CultureInvariant)]
    private static partial Regex JwtPattern();
}
