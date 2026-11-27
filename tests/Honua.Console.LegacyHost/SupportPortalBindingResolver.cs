using System.Text.RegularExpressions;
using Honua.Console.Contracts;

namespace Honua.Console.Web;

internal sealed record SupportPortalBinding(Uri BaseUri, string ApiKey);

internal static partial class SupportPortalBindingResolver
{
    private static readonly HashSet<string> AllowedFleetQueryKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "component",
        "version"
    };

    internal static SupportPortalBinding? TryResolve(IConfiguration configuration)
    {
        var rawBase = ReadFirst(configuration, "HonuaSupport:BaseUrl", "Honua:Support:BaseUrl", "HONUA_SUPPORT_BASE_URL");
        var apiKey = ReadFirst(configuration, "HonuaSupport:ApiKey", "Honua:Support:ApiKey", "HONUA_SUPPORT_API_KEY");
        if (string.IsNullOrWhiteSpace(rawBase) || string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

        var baseUri = ParseSupportBaseUri(rawBase.Trim());
        return baseUri is null ? null : new SupportPortalBinding(baseUri, apiKey.Trim());
    }

    internal static bool IsSafeTicketId(string? id) =>
        !string.IsNullOrWhiteSpace(id)
        && id is not ("." or "..")
        && !id.Contains("..", StringComparison.Ordinal)
        && TicketIdPattern().IsMatch(id);

    internal static string? BuildFleetUpstreamPath(IQueryCollection query)
    {
        if (query.Count == 0)
        {
            return SupportPortalRoutes.UpstreamFleetInstances;
        }

        var pairs = new List<string>();
        foreach (var item in query)
        {
            if (!AllowedFleetQueryKeys.Contains(item.Key) || item.Value.Count != 1)
            {
                return null;
            }

            var value = item.Value[0]?.Trim();
            if (string.IsNullOrWhiteSpace(value) || value.Length > 128 || value.Any(char.IsControl))
            {
                return null;
            }

            pairs.Add($"{Uri.EscapeDataString(item.Key)}={Uri.EscapeDataString(value)}");
        }

        return pairs.Count == 0
            ? SupportPortalRoutes.UpstreamFleetInstances
            : $"{SupportPortalRoutes.UpstreamFleetInstances}?{string.Join('&', pairs)}";
    }

    internal static Uri? ParseSupportBaseUri(string raw)
    {
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var parsed)
            || parsed.UserInfo.Length != 0 || parsed.Query.Length != 0 || parsed.Fragment.Length != 0)
        {
            return null;
        }

        var allowedScheme = string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || (string.Equals(parsed.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) && parsed.IsLoopback);
        return allowedScheme ? new Uri(parsed.AbsoluteUri.TrimEnd('/') + "/") : null;
    }

    private static string? ReadFirst(IConfiguration configuration, params string[] keys)
    {
        foreach (var key in keys)
        {
            var value = configuration[key];
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    [GeneratedRegex("^[A-Za-z0-9._-]{1,128}$", RegexOptions.CultureInvariant)]
    private static partial Regex TicketIdPattern();
}
