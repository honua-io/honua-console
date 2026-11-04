using System.Globalization;
using System.Text.Json;

namespace Honua.Console.Shell.Validation;

public static class BboxParser
{
    public readonly record struct Bbox(double MinX, double MinY, double MaxX, double MaxY);

    public enum BboxError
    {
        None,

        Malformed,

        XOrder,

        YOrder,
    }

    public static bool TryParse(string? value, out Bbox bbox, out BboxError error)
    {
        bbox = default;
        error = BboxError.Malformed;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Split(',');
        if (parts.Length != 4)
        {
            return false;
        }

        var numbers = new double[4];
        for (var i = 0; i < 4; i++)
        {
            if (!double.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out numbers[i]))
            {
                return false;
            }
        }

        var candidate = new Bbox(numbers[0], numbers[1], numbers[2], numbers[3]);

        if (candidate.MinX > candidate.MaxX)
        {
            error = BboxError.XOrder;
            return false;
        }

        if (candidate.MinY > candidate.MaxY)
        {
            error = BboxError.YOrder;
            return false;
        }

        bbox = candidate;
        error = BboxError.None;
        return true;
    }
}

public static class CrsFormat
{
    public static bool IsValid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();

        if (trimmed.StartsWith("EPSG:", StringComparison.OrdinalIgnoreCase))
        {
            return IsPositiveCode(trimmed["EPSG:".Length..]);
        }

        // OGC URI form: .../def/crs/EPSG/0/<code>
        const string ogcMarker = "/def/crs/";
        var markerIndex = trimmed.IndexOf(ogcMarker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex >= 0
            && (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
        {
            var tail = trimmed[(markerIndex + ogcMarker.Length)..];
            var segments = tail.Split('/', StringSplitOptions.RemoveEmptyEntries);
            // Expect <authority>/<version>/<code>, e.g. EPSG/0/4326.
            return segments.Length == 3 && IsPositiveCode(segments[^1]);
        }

        return false;
    }

    private static bool IsPositiveCode(string code) =>
        int.TryParse(code, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n > 0;
}

public static class IsoDateRule
{
    public static bool TryParse(string? value, out DateTimeOffset parsed)
    {
        parsed = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return DateTimeOffset.TryParse(
            value.Trim(),
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out parsed);
    }

    public enum RangeError
    {
        None,

        FromUnparseable,

        ToUnparseable,

        Inverted,
    }

    public static RangeError CheckRange(string? from, string? to)
    {
        var hasFrom = !string.IsNullOrWhiteSpace(from);
        var hasTo = !string.IsNullOrWhiteSpace(to);

        DateTimeOffset parsedFrom = default;
        DateTimeOffset parsedTo = default;

        if (hasFrom && !TryParse(from, out parsedFrom))
        {
            return RangeError.FromUnparseable;
        }

        if (hasTo && !TryParse(to, out parsedTo))
        {
            return RangeError.ToUnparseable;
        }

        if (hasFrom && hasTo && parsedFrom > parsedTo)
        {
            return RangeError.Inverted;
        }

        return RangeError.None;
    }

    public static bool IsInFuture(DateTimeOffset instant, DateTimeOffset now) => instant > now;
}

public static class NumericBoundsRule
{
    public static bool IsWithin(double value, double? min = null, double? max = null)
    {
        if (min.HasValue && value < min.Value)
        {
            return false;
        }

        if (max.HasValue && value > max.Value)
        {
            return false;
        }

        return true;
    }

    public static bool IsOrdered(double? min, double? max) =>
        !min.HasValue || !max.HasValue || min.Value <= max.Value;
}

public static class JsonPointer
{
    public static IReadOnlyList<string> Split(string? pointer)
    {
        if (string.IsNullOrWhiteSpace(pointer))
        {
            return Array.Empty<string>();
        }

        var trimmed = pointer.Trim();
        if (!trimmed.StartsWith('/'))
        {
            // Tolerate a non-rooted locator (a bare token); treat the whole thing as one segment.
            return [Unescape(trimmed)];
        }

        return [.. trimmed[1..]
            .Split('/')
            .Where(token => token.Length > 0)
            .Select(Unescape)];
    }

    // Replace ~1 before ~0 so an encoded tilde survives.
    private static string Unescape(string token) =>
        token.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);
}

public static class CronRule
{
    private static readonly (int Min, int Max)[] FieldRanges =
    [
        (0, 59),   // minute
        (0, 23),   // hour
        (1, 31),   // day of month
        (1, 12),   // month
        (0, 7),    // day of week (0 and 7 are both Sunday)
    ];

    private static readonly HashSet<string> MonthNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "JAN", "FEB", "MAR", "APR", "MAY", "JUN", "JUL", "AUG", "SEP", "OCT", "NOV", "DEC",
    };

    private static readonly HashSet<string> DayNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "SUN", "MON", "TUE", "WED", "THU", "FRI", "SAT",
    };

    public static bool IsValid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var fields = value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length != 5)
        {
            return false;
        }

        for (var i = 0; i < 5; i++)
        {
            if (!IsFieldValid(fields[i], FieldRanges[i].Min, FieldRanges[i].Max, i))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsFieldValid(string field, int min, int max, int fieldIndex)
    {
        // Each comma-separated term is independently valid.
        foreach (var term in field.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            // Optional step suffix: <range>/<step>.
            var slash = term.Split('/');
            if (slash.Length > 2)
            {
                return false;
            }

            if (slash.Length == 2 && (!int.TryParse(slash[1], out var step) || step <= 0))
            {
                return false;
            }

            var range = slash[0];
            if (range == "*")
            {
                continue;
            }

            // A hyphen range a-b, or a single value.
            var bounds = range.Split('-');
            if (bounds.Length > 2)
            {
                return false;
            }

            foreach (var bound in bounds)
            {
                if (!IsValueInRange(bound, min, max, fieldIndex))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool IsValueInRange(string token, int min, int max, int fieldIndex)
    {
        if (string.IsNullOrEmpty(token))
        {
            return false;
        }

        // Named months / days are allowed in their respective fields.
        if (fieldIndex == 3 && MonthNames.Contains(token))
        {
            return true;
        }

        if (fieldIndex == 4 && DayNames.Contains(token))
        {
            return true;
        }

        return int.TryParse(token, System.Globalization.NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            && value >= min && value <= max;
    }
}

public static class UrlRule
{
    public static bool IsAbsoluteHttps(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
            && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsAbsoluteHttp(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
            && (string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                || string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase));
    }
}

public static class IdentifierRule
{
    public static bool IsValid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        return trimmed.All(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.' or ':');
    }
}

public static class EmailRule
{
    public static bool IsValid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();

        // Exactly one '@', non-empty local + domain, no whitespace.
        var at = trimmed.IndexOf('@');
        if (at <= 0 || at != trimmed.LastIndexOf('@') || at == trimmed.Length - 1)
        {
            return false;
        }

        if (trimmed.Any(char.IsWhiteSpace))
        {
            return false;
        }

        var local = trimmed[..at];
        var domain = trimmed[(at + 1)..];

        if (local.Length == 0 || domain.StartsWith('.') || domain.EndsWith('.') || !domain.Contains('.'))
        {
            return false;
        }

        // Every dot-separated domain label must be non-empty (rejects "a..b").
        return domain.Split('.').All(label => label.Length > 0);
    }
}

public static class CidrRule
{
    public static bool IsValid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        var slash = trimmed.IndexOf('/');

        var addressPart = slash >= 0 ? trimmed[..slash] : trimmed;
        if (!System.Net.IPAddress.TryParse(addressPart, out var address))
        {
            return false;
        }

        if (slash < 0)
        {
            // Bare address (host route) is allowed.
            return true;
        }

        var prefixPart = trimmed[(slash + 1)..];
        if (!int.TryParse(prefixPart, NumberStyles.None, CultureInfo.InvariantCulture, out var prefix))
        {
            return false;
        }

        var maxPrefix = address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? 128 : 32;
        return prefix >= 0 && prefix <= maxPrefix;
    }
}

public static class GeoJsonRule
{
    private static readonly HashSet<string> GeometryTypes = new(StringComparer.Ordinal)
    {
        "Point",
        "MultiPoint",
        "LineString",
        "MultiLineString",
        "Polygon",
        "MultiPolygon",
        "GeometryCollection",
    };

    public static bool IsValidGeometry(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(value);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("type", out var type)
                || type.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            var typeName = type.GetString();
            if (string.IsNullOrEmpty(typeName) || !GeometryTypes.Contains(typeName))
            {
                return false;
            }

            return string.Equals(typeName, "GeometryCollection", StringComparison.Ordinal)
                ? root.TryGetProperty("geometries", out var geometries) && geometries.ValueKind == JsonValueKind.Array
                : root.TryGetProperty("coordinates", out var coordinates) && coordinates.ValueKind == JsonValueKind.Array;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
