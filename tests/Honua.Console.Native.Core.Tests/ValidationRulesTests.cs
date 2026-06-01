using Honua.Console.Shell.Validation;

namespace Honua.Console.Native.Core.Tests;

/// <summary>
/// Docker-free unit coverage for the Wave-0 shared validation rule helpers, proving the cross-field
/// invariants the catalog flags as the priority class: bbox ordering, ISO temporal from&lt;=to, CRS
/// format, and numeric bounds/ordering.
/// </summary>
public sealed class ValidationRulesTests
{
    [Theory]
    [InlineData("-158.3,21.2,-157.6,21.7")]
    [InlineData("0,0,1,1")]
    [InlineData(" -10 , -5 , 10 , 5 ")] // whitespace-tolerant
    [InlineData("0,0,0,0")] // degenerate but ordered (min == max)
    public void Bbox_OrderedTuple_Parses(string value)
    {
        var ok = BboxParser.TryParse(value, out var bbox, out var error);

        Assert.True(ok);
        Assert.Equal(BboxParser.BboxError.None, error);
        Assert.True(bbox.MinX <= bbox.MaxX);
        Assert.True(bbox.MinY <= bbox.MaxY);
    }

    [Fact]
    public void Bbox_InvertedX_FailsWithXOrder()
    {
        var ok = BboxParser.TryParse("10,0,-10,5", out _, out var error);

        Assert.False(ok);
        Assert.Equal(BboxParser.BboxError.XOrder, error);
    }

    [Fact]
    public void Bbox_InvertedY_FailsWithYOrder()
    {
        var ok = BboxParser.TryParse("0,10,5,-10", out _, out var error);

        Assert.False(ok);
        Assert.Equal(BboxParser.BboxError.YOrder, error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0,0,1")] // too few
    [InlineData("0,0,1,1,1")] // too many
    [InlineData("a,b,c,d")] // non-numeric
    public void Bbox_Malformed_Fails(string value)
    {
        var ok = BboxParser.TryParse(value, out _, out var error);

        Assert.False(ok);
        Assert.Equal(BboxParser.BboxError.Malformed, error);
    }

    [Theory]
    [InlineData("EPSG:4326")]
    [InlineData("epsg:3857")]
    [InlineData("http://www.opengis.net/def/crs/EPSG/0/4326")]
    [InlineData("https://www.opengis.net/def/crs/EPSG/0/3857")]
    public void Crs_ValidForms_Pass(string value) => Assert.True(CrsFormat.IsValid(value));

    [Theory]
    [InlineData("")]
    [InlineData("4326")] // no authority
    [InlineData("EPSG:")] // no code
    [InlineData("EPSG:abc")] // non-numeric code
    [InlineData("EPSG:0")] // non-positive
    [InlineData("EPSG:-1")]
    [InlineData("www.opengis.net/def/crs/EPSG/0/4326")] // not absolute http(s)
    public void Crs_InvalidForms_Fail(string value) => Assert.False(CrsFormat.IsValid(value));

    [Fact]
    public void IsoDate_OrderedRange_Passes()
    {
        Assert.Equal(IsoDateRule.RangeError.None, IsoDateRule.CheckRange("2024-01-01", "2024-12-31"));
        Assert.Equal(IsoDateRule.RangeError.None, IsoDateRule.CheckRange("2024-01-01T00:00:00Z", "2024-01-01T01:00:00Z"));
    }

    [Fact]
    public void IsoDate_EqualEnds_Passes() =>
        Assert.Equal(IsoDateRule.RangeError.None, IsoDateRule.CheckRange("2024-06-01", "2024-06-01"));

    [Fact]
    public void IsoDate_InvertedRange_Fails() =>
        Assert.Equal(IsoDateRule.RangeError.Inverted, IsoDateRule.CheckRange("2024-12-31", "2024-01-01"));

    [Fact]
    public void IsoDate_OpenEnds_SkipOrdering()
    {
        Assert.Equal(IsoDateRule.RangeError.None, IsoDateRule.CheckRange(null, "2024-01-01"));
        Assert.Equal(IsoDateRule.RangeError.None, IsoDateRule.CheckRange("2024-01-01", null));
        Assert.Equal(IsoDateRule.RangeError.None, IsoDateRule.CheckRange(null, null));
    }

    [Theory]
    [InlineData("not-a-date", "2024-01-01", IsoDateRule.RangeError.FromUnparseable)]
    [InlineData("2024-01-01", "nope", IsoDateRule.RangeError.ToUnparseable)]
    public void IsoDate_Unparseable_Fails(string from, string to, IsoDateRule.RangeError expected) =>
        Assert.Equal(expected, IsoDateRule.CheckRange(from, to));

    [Theory]
    [InlineData(5, 1, 32, true)]
    [InlineData(1, 1, 32, true)]
    [InlineData(32, 1, 32, true)]
    [InlineData(0, 1, 32, false)]
    [InlineData(33, 1, 32, false)]
    public void NumericBounds_Inclusive(double value, double min, double max, bool expected) =>
        Assert.Equal(expected, NumericBoundsRule.IsWithin(value, min, max));

    [Fact]
    public void NumericBounds_OpenEnds()
    {
        Assert.True(NumericBoundsRule.IsWithin(-100, min: null, max: 0));
        Assert.True(NumericBoundsRule.IsWithin(100, min: 1, max: null));
        Assert.False(NumericBoundsRule.IsWithin(-1, min: 0));
    }

    [Theory]
    [InlineData(1.0, 10.0, true)]
    [InlineData(5.0, 5.0, true)] // equal is ordered
    [InlineData(10.0, 1.0, false)]
    public void NumericBounds_OrderingRule(double min, double max, bool expected) =>
        Assert.Equal(expected, NumericBoundsRule.IsOrdered(min, max));

    [Fact]
    public void NumericBounds_OpenOrderingIsAlwaysOrdered()
    {
        Assert.True(NumericBoundsRule.IsOrdered(null, 5));
        Assert.True(NumericBoundsRule.IsOrdered(5, null));
        Assert.True(NumericBoundsRule.IsOrdered(null, null));
    }

    [Theory]
    [InlineData("0 6 * * *")]
    [InlineData("*/15 * * * *")]
    [InlineData("0 0,12 1-15 * MON-FRI")]
    [InlineData("30 9 * JAN,DEC SUN")]
    public void Cron_AcceptsWellFormedExpressions(string expression) =>
        Assert.True(CronRule.IsValid(expression));

    [Theory]
    [InlineData("")]
    [InlineData("not a cron")]
    [InlineData("0 6 * *")]          // only four fields
    [InlineData("0 6 * * * *")]      // six fields
    [InlineData("60 6 * * *")]       // minute out of range
    [InlineData("0 24 * * *")]       // hour out of range
    [InlineData("0 6 0 * *")]        // day-of-month below 1
    [InlineData("0 6 * 13 *")]       // month out of range
    [InlineData("0 6 * * 8")]        // day-of-week out of range
    [InlineData("*/0 6 * * *")]      // zero step
    public void Cron_RejectsMalformedExpressions(string expression) =>
        Assert.False(CronRule.IsValid(expression));
}
