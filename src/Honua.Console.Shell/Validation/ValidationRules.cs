using System.Globalization;

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
