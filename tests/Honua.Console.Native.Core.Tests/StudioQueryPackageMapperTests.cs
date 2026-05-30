using System.Text.Json;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.Native.Core.Tests;

/// <summary>
/// Round-trip coverage for the query-builder mapper (honua-console#52): lowering an authored query into the
/// server saved-query content graph (honua-server#1182) and lifting a loaded version back, plus the preview
/// projection. Asserts the Console editor stays a faithful thin projection over the server contract.
/// </summary>
public sealed class StudioQueryPackageMapperTests
{
    [Fact]
    public void ToSavedQueryContent_LowersSourceBindingProjectionAndComparisonClause()
    {
        var query = new StudioQueryEditor
        {
            Title = "Flood permits",
            ServiceName = "permits",
            LayerId = 5,
            NaturalLanguageQuery = "approved permits",
            OutputSrid = 3857,
            PreviewLimit = 10
        };
        query.OutFields.Add("permit_id");
        query.Predicates.Add(new StudioQueryPredicateEditor
        {
            Kind = StudioQueryPredicateKinds.Comparison,
            Field = "status",
            Operator = "=",
            Value = "approved"
        });

        var content = StudioQueryPackageMapper.ToSavedQueryContent(query);

        Assert.Equal(5, content.LayerId);
        Assert.Equal("permits", content.ServiceName);
        Assert.Equal(3857, content.OutputSrid);
        Assert.Equal(10, content.PreviewLimit);
        Assert.Contains("permit_id", content.OutFields);
        Assert.NotNull(content.FilterPlan);
        var clause = Assert.Single(content.FilterPlan!.Clauses);
        Assert.Equal(HonuaFilterClauseTypes.Comparison, clause.Type);
        Assert.Equal("status", clause.Comparison!.Property);
        Assert.Equal("approved", clause.Comparison.Value!.Value.GetString());
    }

    [Fact]
    public void ToSavedQueryContent_InOperator_SerializesValueAsArray()
    {
        var query = new StudioQueryEditor { LayerId = 1 };
        query.Predicates.Add(new StudioQueryPredicateEditor
        {
            Kind = StudioQueryPredicateKinds.Comparison,
            Field = "zone",
            Operator = "in",
            Value = "A, B, C"
        });

        var content = StudioQueryPackageMapper.ToSavedQueryContent(query);

        var clause = Assert.Single(content.FilterPlan!.Clauses);
        Assert.Equal(JsonValueKind.Array, clause.Comparison!.Value!.Value.ValueKind);
        Assert.Equal(3, clause.Comparison.Value.Value.GetArrayLength());
    }

    [Fact]
    public void ToSavedQueryContent_NumberAndBoolean_ProduceTypedJson()
    {
        var query = new StudioQueryEditor { LayerId = 1 };
        query.Predicates.Add(new StudioQueryPredicateEditor { Field = "pop", Operator = ">", Value = "50000" });
        query.Predicates.Add(new StudioQueryPredicateEditor { Field = "active", Operator = "=", Value = "true" });

        var content = StudioQueryPackageMapper.ToSavedQueryContent(query);

        Assert.Equal(JsonValueKind.Number, content.FilterPlan!.Clauses[0].Comparison!.Value!.Value.ValueKind);
        Assert.Equal(JsonValueKind.True, content.FilterPlan.Clauses[1].Comparison!.Value!.Value.ValueKind);
    }

    [Fact]
    public void ToSavedQueryContent_SpatialDwithin_CarriesDistanceAndUnit()
    {
        var query = new StudioQueryEditor { LayerId = 1 };
        query.Predicates.Add(new StudioQueryPredicateEditor
        {
            Kind = StudioQueryPredicateKinds.Spatial,
            Operator = "dwithin",
            Geometry = "{\"type\":\"Point\",\"coordinates\":[0,0]}",
            Value = "500",
            DistanceUnit = "meters"
        });

        var content = StudioQueryPackageMapper.ToSavedQueryContent(query);

        var spatial = Assert.Single(content.FilterPlan!.Clauses).Spatial;
        Assert.NotNull(spatial);
        Assert.Equal("dwithin", spatial!.Operator);
        Assert.Equal(500, spatial.Distance);
        Assert.Equal("meters", spatial.DistanceUnit);
        Assert.NotNull(spatial.Geometry);
    }

    [Fact]
    public void ToSavedQueryContent_NoPredicates_ProducesNullFilterPlan()
    {
        var content = StudioQueryPackageMapper.ToSavedQueryContent(new StudioQueryEditor { LayerId = 1 });

        Assert.Null(content.FilterPlan);
    }

    [Fact]
    public void ToEditorState_RoundTripsClausesProjectionAndParameters()
    {
        var response = new HonuaAnalysisContentVersionResponse
        {
            Item = new HonuaAnalysisContentItem { ItemId = "query-1", Name = "q", Title = "Permits" },
            Version = new HonuaAnalysisContentVersion
            {
                ItemId = "query-1",
                Version = 2,
                Kind = HonuaAnalysisContentKinds.SavedQuery,
                ContentHash = "hash-2",
                SavedQuery = new HonuaSavedQueryContent
                {
                    LayerId = 7,
                    ServiceName = "permits",
                    OutFields = ["a", "b"],
                    OutputFormat = "csv",
                    FilterPlan = new HonuaFilterPlan
                    {
                        Combinator = HonuaFilterPlanCombinators.Or,
                        Clauses =
                        [
                            new HonuaFilterPlanClause
                            {
                                Type = HonuaFilterClauseTypes.Temporal,
                                Temporal = new HonuaTemporalClause { Property = "issued", Operator = "after", Start = "2024-01-01" }
                            }
                        ]
                    },
                    Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["console.title"] = "Permits",
                        ["console.parameters"] = "minYear=2024;zone=A"
                    }
                }
            }
        };

        var editor = StudioQueryPackageMapper.ToEditorState(response);

        Assert.Equal("query-1", editor.QueryId);
        Assert.Equal(2, editor.Version);
        Assert.Equal("hash-2", editor.ETag);
        Assert.Equal(7, editor.LayerId);
        Assert.Equal("csv", editor.OutputFormat);
        Assert.Equal(StudioQueryCombinators.Or, editor.Combinator);
        Assert.Equal(2, editor.OutFields.Count);
        var temporal = Assert.Single(editor.Predicates);
        Assert.Equal(StudioQueryPredicateKinds.Temporal, temporal.Kind);
        Assert.Equal("issued", temporal.Field);
        Assert.Equal(2, editor.Parameters.Count);
        Assert.Contains(editor.Parameters, p => p.Name == "minYear" && p.Value == "2024");
    }

    [Fact]
    public void ToPreview_ProjectsColumnsFeaturesAndDownstreamTargets()
    {
        var result = new HonuaSavedQueryPreviewResult
        {
            PreviewArtifactId = "preview-1",
            ItemId = "query-1",
            Version = 1,
            LayerId = 3,
            TotalCount = 2,
            Features =
            [
                new HonuaSavedQueryPreviewFeature
                {
                    Id = 1,
                    HasGeometry = true,
                    Attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                    {
                        ["name"] = JsonSerializer.SerializeToElement("Site A"),
                        ["pop"] = JsonSerializer.SerializeToElement(1200)
                    }
                }
            ]
        };

        var preview = StudioQueryPackageMapper.ToPreview(result);

        Assert.Equal(3, preview.LayerId);
        Assert.Equal(1, preview.FeatureCount);
        Assert.True(preview.HasGeometry);
        Assert.Equal(["name", "pop"], preview.Columns);
        Assert.Equal("Site A", preview.Features[0].Attributes["name"]);
        Assert.Equal("1200", preview.Features[0].Attributes["pop"]);
        // A geometry-bearing query can feed a map.
        Assert.Contains("map", preview.DownstreamTargets);
    }

    [Fact]
    public void ResolveDownstreamTargets_WithoutGeometry_OmitsMap()
    {
        var targets = StudioQueryPackageMapper.ResolveDownstreamTargets(hasGeometry: false);

        Assert.DoesNotContain("map", targets);
        Assert.Contains("dashboard", targets);
        Assert.Contains("workflow", targets);
    }
}
