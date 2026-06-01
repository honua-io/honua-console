using System.Globalization;
using System.Text.Json;

namespace Honua.Console.Shell.Validation;

/// <summary>
/// Parses a console bounding-box string of the form <c>"minX,minY,maxX,maxY"</c> and enforces the
/// ordering invariant <c>minX &lt;= maxX</c> and <c>minY &lt;= maxY</c> (the same rule honua-server
/// enforces as <c>studio.map.initial-view.bbox.order</c>). Pure and culture-invariant so the same
/// result is produced regardless of the operator's locale.
/// </summary>
public static class BboxParser
{
    /// <summary>A successfully parsed and ordered bounding box.</summary>
    public readonly record struct Bbox(double MinX, double MinY, double MaxX, double MaxY);

    /// <summary>The reason a bbox string failed to parse / validate, or <see cref="None"/> when valid.</summary>
    public enum BboxError
    {
        /// <summary>The box parsed and is correctly ordered.</summary>
        None,

        /// <summary>The string did not contain exactly four comma-separated numeric components.</summary>
        Malformed,

        /// <summary><c>minX &gt; maxX</c> — the x extent is inverted.</summary>
        XOrder,

        /// <summary><c>minY &gt; maxY</c> — the y extent is inverted.</summary>
        YOrder,
    }

    /// <summary>
    /// Attempts to parse <paramref name="value"/> into an ordered <see cref="Bbox"/>. Returns
    /// <see langword="true"/> only when the string is four invariant-culture numbers AND correctly
    /// ordered; otherwise <paramref name="error"/> explains why.
    /// </summary>
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

/// <summary>
/// Validates a coordinate reference system identifier against the two shapes honua-server accepts
/// (server <c>IsValidCrs</c>): the short <c>EPSG:&lt;n&gt;</c> authority form and the OGC URI form
/// <c>http(s)://www.opengis.net/def/crs/EPSG/0/&lt;n&gt;</c>.
/// </summary>
public static class CrsFormat
{
    /// <summary>Returns <see langword="true"/> when <paramref name="value"/> is a recognised CRS identifier.</summary>
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

/// <summary>
/// Parses ISO-8601 date/datetime strings and enforces the temporal ordering invariant
/// <c>from &lt;= to</c> (query predicate Start/End, Operate diff From/To checkpoints, etc.). Uses
/// round-trip / invariant parsing so the same result is produced regardless of locale.
/// </summary>
public static class IsoDateRule
{
    /// <summary>Attempts to parse an ISO-8601 date or datetime (optionally offset-bearing).</summary>
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

    /// <summary>The result of an ISO from/to range check.</summary>
    public enum RangeError
    {
        /// <summary>Both ends parsed and <c>from &lt;= to</c>.</summary>
        None,

        /// <summary>The <c>from</c> string is not a parseable ISO-8601 instant.</summary>
        FromUnparseable,

        /// <summary>The <c>to</c> string is not a parseable ISO-8601 instant.</summary>
        ToUnparseable,

        /// <summary><c>from &gt; to</c> — the range is inverted.</summary>
        Inverted,
    }

    /// <summary>
    /// Validates that <paramref name="from"/> and <paramref name="to"/> are both ISO-8601 instants and
    /// that <c>from &lt;= to</c>. Empty ends are treated as "unbounded" and skip the ordering check.
    /// </summary>
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

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="instant"/> is strictly after <paramref name="now"/>
    /// (the share public-link / embed expiry must be in the future). The caller supplies <paramref name="now"/>
    /// so the rule stays pure and unit-testable; default to <see cref="DateTimeOffset.UtcNow"/> at the call site.
    /// </summary>
    public static bool IsInFuture(DateTimeOffset instant, DateTimeOffset now) => instant > now;
}

/// <summary>
/// Inclusive numeric-bounds helper for the many value-range rules in the validation matrix (srid
/// positive, worker Cpu 1-32, MaxAttempts 0-10, PreviewLimit&gt;=1, retention&gt;0, …) plus the
/// cross-field <c>min &lt;= max</c> ordering rule (form RangeMin/RangeMax).
/// </summary>
public static class NumericBoundsRule
{
    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="value"/> is within the inclusive
    /// <paramref name="min"/>..<paramref name="max"/> window. Pass <see langword="null"/> for an
    /// unbounded end.
    /// </summary>
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

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="min"/> &lt;= <paramref name="max"/>. When
    /// either bound is <see langword="null"/> the range is considered open (and therefore ordered).
    /// </summary>
    public static bool IsOrdered(double? min, double? max) =>
        !min.HasValue || !max.HasValue || min.Value <= max.Value;
}

/// <summary>
/// Parses an RFC-6901 JSON Pointer (the <c>path</c> on a server validation diagnostic, e.g.
/// <c>/body/layers/2/sourceRef</c>) into its decoded reference tokens. Per-editor pointer resolvers walk
/// the returned tokens to map a server diagnostic onto a console field key.
/// </summary>
public static class JsonPointer
{
    /// <summary>
    /// Splits <paramref name="pointer"/> into its decoded reference tokens (with the RFC-6901 <c>~1</c> →
    /// <c>/</c> and <c>~0</c> → <c>~</c> escapes applied). A null/empty/whitespace pointer, or the root
    /// pointer <c>""</c>/<c>"/"</c>, yields an empty list.
    /// </summary>
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

    private static string Unescape(string token) =>
        token.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);
}

/// <summary>
/// Validates a standard 5-field cron expression (minute hour day-of-month month day-of-week). A shape /
/// range gate only: each field is either <c>*</c>, a <c>*/step</c> step, a comma list, a hyphen range, or
/// a single value, and every numeric value is within the field's allowed range. Names (JAN, MON, …) are
/// accepted for the month and day-of-week fields. Pure and culture-invariant. The server owns the
/// authoritative schedule semantics; this catches obviously-malformed operator input before submit.
/// </summary>
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

    /// <summary>Returns <see langword="true"/> when <paramref name="value"/> is a well-formed 5-field cron expression.</summary>
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

/// <summary>
/// Validates a URL string against the absolute-https requirement the console enforces for a server base URI
/// (env <c>ServerBaseUri</c>). Pure: returns <see langword="true"/> only when the value parses as an absolute
/// URI whose scheme is <c>https</c>. The catalog's "URL absolute-https" rule.
/// </summary>
public static class UrlRule
{
    /// <summary>Returns <see langword="true"/> when <paramref name="value"/> is an absolute https URL.</summary>
    public static bool IsAbsoluteHttps(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
            && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="value"/> is an absolute http or https URL. Used
    /// by intake surfaces (e.g. the Esri import URL / item-id field) that accept either scheme; the
    /// authoritative fetch is owned by honua-server.
    /// </summary>
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

/// <summary>
/// Validates a resource identifier / lookup-id token (Share <c>ItemId</c>, publication <c>LookupId</c>). A
/// permissive shape gate: non-empty, no whitespace, and built only from letters, digits, and the separator
/// set <c>- _ . :</c> (the console's content-ref / publication-id alphabet). The server owns canonical
/// resolution; this just catches obviously-malformed operator input (spaces, control chars) before submit.
/// Pure and culture-invariant.
/// </summary>
public static class IdentifierRule
{
    /// <summary>Returns <see langword="true"/> when <paramref name="value"/> is a well-formed identifier token.</summary>
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

/// <summary>
/// Lightweight client-side check that a string is a syntactically plausible email address (the RBAC
/// scoped-invite identity). A shape gate only — it confirms the value has a single <c>@</c> with a
/// non-empty local part and a dotted domain whose labels are non-empty — not deliverability, which the
/// server owns. Pure and culture-invariant. A leading/trailing whitespace is tolerated (trimmed).
/// </summary>
public static class EmailRule
{
    /// <summary>Returns <see langword="true"/> when <paramref name="value"/> is a plausible email address.</summary>
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

/// <summary>
/// Validates a CIDR block (the RBAC IP allowlist entries). Accepts both IPv4 and IPv6 in
/// <c>address/prefix</c> form, with the prefix length within the address family's valid range (0..32 for
/// IPv4, 0..128 for IPv6). A bare address with no <c>/prefix</c> is also accepted (treated as a host
/// route). Pure and culture-invariant; the server owns authoritative allowlist semantics.
/// </summary>
public static class CidrRule
{
    /// <summary>Returns <see langword="true"/> when <paramref name="value"/> is a well-formed CIDR block or bare IP.</summary>
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

/// <summary>
/// Lightweight client-side check that a string is a parseable GeoJSON geometry literal. This is a shape
/// gate only — it confirms the value is a JSON object carrying a recognised geometry <c>type</c> and a
/// <c>coordinates</c> (or, for a GeometryCollection, <c>geometries</c>) member — not full topology
/// validation, which honua-server owns. Pure and culture-invariant so the same result is produced
/// regardless of locale; used by the query builder's spatial predicates.
/// </summary>
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

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="value"/> parses as a JSON object whose
    /// <c>type</c> is a recognised GeoJSON geometry and which carries the matching geometry payload
    /// (<c>coordinates</c>, or <c>geometries</c> for a GeometryCollection). Blank input returns
    /// <see langword="false"/>.
    /// </summary>
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
