using Honua.Console.Shell.Models;
using Honua.Console.Shell.Validation;

namespace Honua.Console.Native.Core.Tests;

/// <summary>
/// Docker-free unit coverage for <see cref="StudioQueryValidator"/>, the Wave-2 client cross-field/bounds/
/// format validator for the Studio query builder. Each presence, bounds, temporal-ordering, dwithin-distance,
/// and GeoJSON rule from the catalog is proven in its pass and fail state, keyed by
/// <see cref="StudioQueryFieldKeys"/>.
/// </summary>
public sealed class StudioQueryValidatorTests
{
    private static IReadOnlyList<ConsoleFieldError> Evaluate(StudioQueryEditor state) =>
        StudioQueryValidator.Instance.Evaluate(state);

    /// <summary>A fully valid baseline query so a single failing rule can be asserted in isolation.</summary>
    private static StudioQueryEditor Valid() => new()
    {
        ServiceName = "permits",
        LayerId = 0,
        PreviewLimit = 25,
    };

    [Fact]
    public void ValidQuery_ProducesNoErrors() => Assert.Empty(Evaluate(Valid()));

    [Fact]
    public void MissingServiceName_BlocksOnServiceNameKey()
    {
        var state = Valid();
        state.ServiceName = "  ";

        var error = Assert.Single(Evaluate(state), e => e.FieldKey == StudioQueryFieldKeys.ServiceName);
        Assert.Equal(ConsoleValidationSeverity.Blocker, error.Severity);
    }

    [Fact]
    public void NegativeLayerId_BlocksOnLayerIdKey()
    {
        var state = Valid();
        state.LayerId = -1;

        Assert.Contains(Evaluate(state), e =>
            e.FieldKey == StudioQueryFieldKeys.LayerId && e.Severity == ConsoleValidationSeverity.Blocker);
    }

    [Fact]
    public void NonNegativeLayerId_Passes()
    {
        var state = Valid();
        state.LayerId = 7;

        Assert.DoesNotContain(Evaluate(state), e => e.FieldKey == StudioQueryFieldKeys.LayerId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-4326)]
    public void NonPositiveOutputSrid_Flags(int srid)
    {
        var state = Valid();
        state.OutputSrid = srid;

        Assert.Contains(Evaluate(state), e => e.FieldKey == StudioQueryFieldKeys.OutputSrid);
    }

    [Fact]
    public void PositiveOutputSrid_Passes()
    {
        var state = Valid();
        state.OutputSrid = 4326;

        Assert.DoesNotContain(Evaluate(state), e => e.FieldKey == StudioQueryFieldKeys.OutputSrid);
    }

    [Fact]
    public void NullOutputSrid_Passes()
    {
        var state = Valid();
        state.OutputSrid = null;

        Assert.DoesNotContain(Evaluate(state), e => e.FieldKey == StudioQueryFieldKeys.OutputSrid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void PreviewLimitBelowOne_Flags(int limit)
    {
        var state = Valid();
        state.PreviewLimit = limit;

        Assert.Contains(Evaluate(state), e => e.FieldKey == StudioQueryFieldKeys.PreviewLimit);
    }

    [Fact]
    public void TemporalFromBeforeTo_Passes()
    {
        var state = Valid();
        state.Predicates.Add(new StudioQueryPredicateEditor
        {
            Kind = StudioQueryPredicateKinds.Temporal,
            Start = "2024-01-01",
            End = "2024-12-31",
        });

        Assert.DoesNotContain(Evaluate(state), e => e.FieldKey == StudioQueryFieldKeys.PredicateRange(0));
    }

    [Fact]
    public void TemporalFromAfterTo_FlagsOrder()
    {
        var state = Valid();
        state.Predicates.Add(new StudioQueryPredicateEditor
        {
            Kind = StudioQueryPredicateKinds.Temporal,
            Start = "2024-12-31",
            End = "2024-01-01",
        });

        var error = Assert.Single(Evaluate(state), e => e.FieldKey == StudioQueryFieldKeys.PredicateRange(0));
        Assert.Equal("query.predicate.temporal.order", error.Code);
    }

    [Fact]
    public void TemporalUnparseableStart_FlagsFormat()
    {
        var state = Valid();
        state.Predicates.Add(new StudioQueryPredicateEditor
        {
            Kind = StudioQueryPredicateKinds.Temporal,
            Start = "not-a-date",
            End = "2024-12-31",
        });

        var error = Assert.Single(Evaluate(state), e => e.FieldKey == StudioQueryFieldKeys.PredicateRange(0));
        Assert.Equal("query.predicate.temporal.format", error.Code);
    }

    [Fact]
    public void DwithinWithPositiveDistance_Passes()
    {
        var state = Valid();
        state.Predicates.Add(new StudioQueryPredicateEditor
        {
            Kind = StudioQueryPredicateKinds.Spatial,
            Operator = "dwithin",
            Value = "500",
            Geometry = "{\"type\":\"Point\",\"coordinates\":[0,0]}",
        });

        Assert.Empty(Evaluate(state));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("")]
    [InlineData("abc")]
    public void DwithinWithoutPositiveDistance_Flags(string value)
    {
        var state = Valid();
        state.Predicates.Add(new StudioQueryPredicateEditor
        {
            Kind = StudioQueryPredicateKinds.Spatial,
            Operator = "dwithin",
            Value = value,
            Geometry = "{\"type\":\"Point\",\"coordinates\":[0,0]}",
        });

        Assert.Contains(Evaluate(state), e => e.FieldKey == StudioQueryFieldKeys.PredicateDistance(0));
    }

    [Fact]
    public void SpatialWithValidGeoJson_Passes()
    {
        var state = Valid();
        state.Predicates.Add(new StudioQueryPredicateEditor
        {
            Kind = StudioQueryPredicateKinds.Spatial,
            Operator = "intersects",
            Geometry = "{\"type\":\"Polygon\",\"coordinates\":[[[0,0],[1,0],[1,1],[0,0]]]}",
        });

        Assert.DoesNotContain(Evaluate(state), e => e.FieldKey == StudioQueryFieldKeys.PredicateGeometry(0));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"type\":\"Banana\",\"coordinates\":[0,0]}")]
    [InlineData("{\"coordinates\":[0,0]}")]
    [InlineData("{\"type\":\"Point\"}")]
    public void SpatialWithInvalidGeoJson_Flags(string geometry)
    {
        var state = Valid();
        state.Predicates.Add(new StudioQueryPredicateEditor
        {
            Kind = StudioQueryPredicateKinds.Spatial,
            Operator = "intersects",
            Geometry = geometry,
        });

        var error = Assert.Single(Evaluate(state), e => e.FieldKey == StudioQueryFieldKeys.PredicateGeometry(0));
        Assert.Equal("query.predicate.geometry.geojson", error.Code);
    }
}
