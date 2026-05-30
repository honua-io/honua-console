using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.Native.Core.Tests;

/// <summary>
/// Coverage for the Console-side SQL/filter readout the query builder shows over the authored source
/// binding and predicates before save (honua-console#52, AC#2). The readout must reflect the bound source
/// and predicates so the operator reviews the query before it is saved on honua-server.
/// </summary>
public sealed class StudioQueryReadoutGeneratorTests
{
    [Fact]
    public void Generate_NoPredicates_SelectsAllFromBoundSource()
    {
        var query = new StudioQueryEditor { ServiceName = "permits", LayerId = 5 };

        var readout = StudioQueryReadoutGenerator.Generate(query);

        Assert.Equal("SELECT * FROM permits/layer/5", readout);
    }

    [Fact]
    public void Generate_ProjectionAndComparison_RendersWhereClause()
    {
        var query = new StudioQueryEditor { ServiceName = "permits", LayerId = 5 };
        query.OutFields.Add("permit_id");
        query.OutFields.Add("status");
        query.Predicates.Add(new StudioQueryPredicateEditor { Field = "status", Operator = "=", Value = "approved" });

        var readout = StudioQueryReadoutGenerator.Generate(query);

        Assert.Equal("SELECT permit_id, status FROM permits/layer/5 WHERE status = 'approved'", readout);
    }

    [Fact]
    public void Generate_NumericValue_IsNotQuoted()
    {
        var query = new StudioQueryEditor { LayerId = 1 };
        query.Predicates.Add(new StudioQueryPredicateEditor { Field = "pop", Operator = ">", Value = "50000" });

        var readout = StudioQueryReadoutGenerator.Generate(query);

        Assert.EndsWith("WHERE pop > 50000", readout);
    }

    [Fact]
    public void Generate_InOperator_RendersQuotedList()
    {
        var query = new StudioQueryEditor { LayerId = 1 };
        query.Predicates.Add(new StudioQueryPredicateEditor { Field = "zone", Operator = "in", Value = "A, B" });

        var readout = StudioQueryReadoutGenerator.Generate(query);

        Assert.EndsWith("WHERE zone IN ('A', 'B')", readout);
    }

    [Fact]
    public void Generate_MultiplePredicates_UsesSelectedCombinator()
    {
        var query = new StudioQueryEditor { LayerId = 1, Combinator = StudioQueryCombinators.Or };
        query.Predicates.Add(new StudioQueryPredicateEditor { Field = "a", Operator = "=", Value = "1" });
        query.Predicates.Add(new StudioQueryPredicateEditor { Field = "b", Operator = "=", Value = "2" });

        var readout = StudioQueryReadoutGenerator.Generate(query);

        Assert.Contains(" OR ", readout, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_SpatialDwithin_RendersDistancePredicate()
    {
        var query = new StudioQueryEditor { LayerId = 1 };
        query.Predicates.Add(new StudioQueryPredicateEditor
        {
            Kind = StudioQueryPredicateKinds.Spatial,
            Operator = "dwithin",
            Value = "500",
            DistanceUnit = "meters"
        });

        var readout = StudioQueryReadoutGenerator.Generate(query);

        Assert.Contains("DWITHIN(geometry, <geometry>, 500 meters)", readout, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_TemporalDuring_RendersBetween()
    {
        var query = new StudioQueryEditor { LayerId = 1 };
        query.Predicates.Add(new StudioQueryPredicateEditor
        {
            Kind = StudioQueryPredicateKinds.Temporal,
            Field = "issued",
            Operator = "during",
            Start = "2024-01-01",
            End = "2024-12-31"
        });

        var readout = StudioQueryReadoutGenerator.Generate(query);

        Assert.Contains("issued BETWEEN '2024-01-01' AND '2024-12-31'", readout, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_NoServiceName_FallsBackToLayerReference()
    {
        var query = new StudioQueryEditor { LayerId = 9 };

        var readout = StudioQueryReadoutGenerator.Generate(query);

        Assert.Equal("SELECT * FROM layer:9", readout);
    }
}
